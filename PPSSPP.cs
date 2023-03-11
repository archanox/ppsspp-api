using System.Diagnostics;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ppsspp_api.Endpoints;
using Websocket.Client;

namespace ppsspp_api;

public class PPSSPP
{
	public enum ErrorLevels
	{
		Unknown = 0,
		
		Notice = 1,
		Error = 2,
		Warn = 3,
		Info = 4,
		Debug = 5,
		Verbose = 6,
	}
	
	const string PpssppMatchApi = "https://report.ppsspp.org/match/list";
	const string PpssppSubProtocol = "debugger.ppsspp.org";
	const string PpssppDefaultPath = "/debugger";
	
	protected internal string ClientName { get; }
	protected internal string ClientVersion { get; }

	public PPSSPP(string clientName, string clientVersion)
	{
		ClientName = clientName;
		ClientVersion = clientVersion;

		Game = new Game(this);
		Cpu = new Cpu(this);
		Memory = new Memory(this);
		Hle = new Hle(this);
		Input = new Input(this);
	}

	public event EventHandler<ResultMessage>? OnError;
	public event EventHandler? OnClose;

	private WebsocketClient? _socket;
	private readonly Dictionary<string, EventHandler<JsonElement>> _pendingTickets = new();

	private readonly string[] _noResponseEvents = { "cpu.stepping", "cpu.resume" };
	
	public readonly Game Game;
	public readonly Cpu Cpu;
	public readonly Memory Memory;
	public readonly Hle Hle;
	public readonly Input Input;
	
	public async Task AutoConnect()
	{
		if (_socket != null)
		{
			throw new Exception("Already connected, disconnect first");
		}

		using HttpClient client = new();
		await using var stream = await client.GetStreamAsync(PpssppMatchApi);
		var listing = await JsonSerializer.DeserializeAsync<Endpoint[]>(stream);
		
		_socket = TryNextEndpoint(listing);

		try
		{
			SetupSocket(_socket);
		}
		catch (Exception e)
		{
			throw new Exception("Couldn't connect", innerException: e);
		}
	}
	
	public WebsocketClient Connect(Uri uri)
	{
		if (_socket != null)
		{
			throw new Exception("Already connected, disconnect first");
		}
		
		var possibleSocket = new WebsocketClient(uri, () =>
				{
					var clientWebSocket = new ClientWebSocket();
					clientWebSocket.Options.AddSubProtocol(PpssppSubProtocol);
					return clientWebSocket;
				}
			)
		;

		possibleSocket.StartOrFail();
		
		if (possibleSocket.IsStarted)
		{
			_socket = possibleSocket;
			SetupSocket(_socket);
			return _socket;
		}

		throw new Exception($"Couldn't connect to {uri}");
	}

	public void Disconnect()
	{
		if (_socket == null)
		{
			throw new Exception("Not Connected");
		}
		
		FailAllPending("Disconnected from PPSSPP");
		
		_socket.Stop(WebSocketCloseStatus.NormalClosure, "Disconnected from PPSSPP");
		_socket.Dispose();
		_socket = null;
		
		OnClose?.Invoke(this, EventArgs.Empty);
	}

	public Task<T> Send<T>(ResultMessage data)
		where T : IMessage
	{
		if (_socket == null)
		{
			throw new Exception("Not connected");
		}

		if (_noResponseEvents.Contains(data.Event))
		{
			_socket.Send(JsonSerializer.Serialize(data));
		}

		var ticket = MakeTicket();
		
		var tcs = new TaskCompletionSource<T>();
		
		_pendingTickets[ticket] = (sender, args) =>
		{
			if (args.GetProperty("event").GetString() == "error")
			{
				var err = new Exception(args.GetProperty("message").GetString())
				{
					Data =
					{
						["ReceivedMessage"] = args.GetRawText(),
					},
				};
				tcs.SetException(err);
			}
			else
			{
				var result = args.Deserialize<T>();
				result.Data = args;
				tcs.SetResult(result);
			}
		};
		
		data.Ticket = ticket;
		
		_socket.Send(JsonSerializer.Serialize(data, new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		}));
		

		return tcs.Task;
	}

	private void SetupSocket(IWebsocketClient? websocketClient)
	{
		websocketClient?.DisconnectionHappened.Subscribe(info =>
			{
				OnClose?.Invoke(this, EventArgs.Empty);
				
				FailAllPending($"PPSSPP disconnected {info.Exception?.Message}");
				_socket?.Dispose();
				_socket = null;
			}
		);

		websocketClient?.MessageReceived.Select(message => Observable.FromAsync(async () =>
			{
				JsonElement root = new();
				try
				{
					using var doc = JsonDocument.Parse(message.Text);
					root = doc.RootElement;
					if (root.GetProperty("event").GetString() == "error")
					{
						await HandleError(root.GetProperty("message").GetString(), (ErrorLevels)root.GetProperty("level").GetUInt32());
					}

					var handled = false;
					if (root.TryGetProperty("ticket", out var ticket))
					{
						if (_pendingTickets.TryGetValue(ticket.GetString(), out var handler))
						{
							_pendingTickets.Remove(ticket.GetString());

							handler.Invoke(this, root);
							handled = true;
						}

						if (handled == false)
						{
							await HandleError("Received mismatched ticket: " + doc, ErrorLevels.Error);
						}
					}

					if (!handled)
					{
						var eventName = root.GetProperty("event").GetString();
						switch (eventName)
						{
							case "game.start":
								Game.Started(root);
								break;
							case "game.quit":
								Game.Quit(root);
								break;
							case "game.resume":
							case "game.pause":
								Game.PauseChanged(root);
								break;
							case "cpu.stepping":
								Cpu.Stepped(root);
								break;
							case "input.buttons":
								Input.ButtonChanged(root);
								break;
							case "input.analog":
								Input.AnalogChanged(root);
								break;
							case "cpu.resume":
								Cpu.Resumed(root);
								break;
							default:
								await Console.Error.WriteLineAsync($"{eventName} is unsupported");
								Debug.WriteLine(root.GetRawText());
								break;
						}
					}
				}
				catch (Exception ex)
				{
					await HandleError($"Failed to parse message from PPSSPP: {ex.Message}", ErrorLevels.Error);
					
					//if(ex.Data != null && ex.Data.Contains("ReceivedMessage") && ex.Data["ReceivedMessage"] is JsonDocument)
                    //		Debug.WriteLine(((JsonDocument)ex.Data["ReceivedMessage"]).RootElement.GetRawText());
					
					Debug.WriteLine(root.GetRawText());
					throw;
				}
			}))
			.Merge()
			.Subscribe();
	}

	private WebsocketClient TryNextEndpoint(IEnumerable<Endpoint>? listing)
	{
		while (true)
		{
			var endpoints = listing as Endpoint[] ?? listing?.ToArray();
			if (endpoints == null || !endpoints.Any())
			{
				throw new Exception("Couldn't connect automatically. Is PPSSPP connected to the same network?");
			}

			var ipAddress = endpoints.First().IpAddress;
			if (ipAddress.Contains(':'))
			{
				ipAddress = $"[{ipAddress}]";
			}

			var endpoint = new Uri($"ws://{ipAddress}:{endpoints.First().Port}{PpssppDefaultPath}");

			var socket = Connect(endpoint);

			if (socket.IsRunning)
				return socket;
			
			if (endpoints.Length > 1)
			{
				listing = endpoints.Skip(1);
				continue;
			}

			break;
		}

		return default!;
	}

	private async Task HandleError(string message, ErrorLevels level = ErrorLevels.Unknown)
	{
		if (OnError != null && OnError.GetInvocationList().Any())
		{
			OnError?.Invoke(this, new ResultMessage
			{
				Message = message,
				Level = level,
			});
		}
		else if (level is ErrorLevels.Unknown or ErrorLevels.Error)
		{
			await Console.Error.WriteLineAsync($"{level.ToString()}: {message}");
		}
		else
		{
			Console.WriteLine($"{level.ToString()}: {message}");
		}
	}

	private string MakeTicket()
	{
		const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
		var random = new Random();
		
		while (true)
		{
			var ticket = new string(Enumerable.Repeat(chars, 11)
				.Select(s => s[random.Next(s.Length)])
				.ToArray());

			if (_pendingTickets.ContainsKey(ticket))
			{
				continue;
			}

			return ticket;
		}
	}

	private void FailAllPending(string message)
	{
		var data = new ResultMessage { Event = "error", Message = message, Level = ErrorLevels.Error };
		
		foreach (var pendingTicket in _pendingTickets.ToArray())
		{
			_pendingTickets[pendingTicket.Key].Invoke(this, JsonSerializer.SerializeToElement(data));
		}
		
		_pendingTickets.Clear();
	}
}


