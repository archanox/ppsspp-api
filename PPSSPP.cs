using System.Diagnostics;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ppsspp_api.Endpoints;
using Websocket.Client;

namespace ppsspp_api;

/// <summary>
/// 
/// </summary>
public class Ppsspp : IAsyncDisposable
{
	/// <summary>
	/// Error levels as reported by the debugger
	/// </summary>
	public enum ErrorLevels
	{
		/// <summary>
		/// Default error level when severity is indeterminate
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// VERY important information that is NOT errors. Like startup and debugprintfs from the game itself.
		/// </summary>
		Notice = 1,
		/// <summary>
		/// Important errors.
		/// </summary>
		Error = 2,
		/// <summary>
		/// Something is suspicious.
		/// </summary>
		Warn = 3,
		/// <summary>
		/// General information.
		/// </summary>
		Info = 4,
		/// <summary>
		/// Detailed debugging - might make things slow.
		/// </summary>
		Debug = 5,
		/// <summary>
		/// Noisy debugging - sometimes needed but usually unimportant.
		/// </summary>
		Verbose = 6,
	}

	private const string PpssppMatchApi = "https://report.ppsspp.org/match/list";
	private const string PpssppSubProtocol = "debugger.ppsspp.org";
	private const string PpssppDefaultPath = "/debugger";

	/// <summary>
	/// string indicating name of app or tool
	/// </summary>
	protected internal string ClientName { get; }

	/// <summary>
	/// string indicating version of app or tool
	/// </summary>
	protected internal string ClientVersion { get; }

	/// <summary>
	/// Requires a client name and version for handshake with the debugger
	/// </summary>
	/// <param name="clientName"><see cref="ClientName"/></param>
	/// <param name="clientVersion"><inheritdoc cref="ClientVersion"/></param>
	public Ppsspp(string clientName, string clientVersion)
	{
		ClientName = clientName;
		ClientVersion = clientVersion;

		Game = new Game(this);
		Cpu = new Cpu(this);
		Memory = new Memory(this);
		Hle = new Hle(this);
		Input = new Input(this);
	}

	/// <summary>
	/// Set this to a function receiving (message, level) for errors.
	/// </summary>
	public event EventHandler<ResultMessage>? OnError;

	/// <summary>
	/// Set this to a function with no parameters called on disconnect.
	/// </summary>
	public event EventHandler? OnClose;

	private WebsocketClient? _socket;
	private readonly Dictionary<string, EventHandler<JsonElement>> _pendingTickets = new();

	private readonly string[] _noResponseEvents = { "cpu.stepping", "cpu.resume" };
	
	/// <inheritdoc cref="Game"/>
	public readonly Game Game;
	/// <inheritdoc cref="Cpu"/>
	public readonly Cpu Cpu;
	/// <inheritdoc cref="Memory"/>
	public readonly Memory Memory;
	/// <inheritdoc cref="Hle"/>
	public readonly Hle Hle;
	/// <inheritdoc cref="Input"/>
	public readonly Input Input;

	/// <summary>
	/// The autoConnect() function tries to find a nearby PPSSPP instance.
	/// If you have multiple, it may be the wrong one.
	/// </summary>
	/// <returns></returns>
	/// <exception cref="Exception">Throws when client is unable to connect to a nearby instance</exception>
	/// <exception cref="AlreadyConnectedException"></exception>
	public async Task AutoConnectAsync()
	{
		if (_socket != null)
		{
			throw new AlreadyConnectedException();
		}

		using var client = new HttpClient();
		await using var stream = await client.GetStreamAsync(PpssppMatchApi);
		var listing = await JsonSerializer.DeserializeAsync<Endpoint[]>(stream);
		
		_socket = await TryNextEndpointAsync(listing);

		try
		{
			SetupSocket(_socket);
		}
		catch (Exception e)
		{
			throw new Exception("Couldn't connect", innerException: e);
		}
	}

	/// <summary>
	/// Connect to a specific WebSocket URI (ie. ws://127.0.0.1:45333/debugger)
	/// </summary>
	/// <param name="uri">The PPSSPP debugger endpoint</param>
	/// <returns>A subscribed <see cref="WebsocketClient"/> with a subprotocol and listeners</returns>
	/// <exception cref="AlreadyConnectedException">Throws when client is already connected to the debugger</exception>
	/// <exception cref="Exception">Throws when client is unable to connect to <paramref name="uri"/></exception>
	public async Task<WebsocketClient> ConnectAsync(Uri uri)
	{
		if (uri.Scheme != "ws")
		{
			throw new UriFormatException("Provided endpoint is not a websocket url");
		}

		if (_socket != null)
		{
			throw new AlreadyConnectedException();
		}
		
		var possibleSocket = new WebsocketClient(uri, () =>
			{
				var clientWebSocket = new ClientWebSocket();
				clientWebSocket.Options.AddSubProtocol(PpssppSubProtocol);
				return clientWebSocket;
			}
		);

		await possibleSocket.StartOrFail();

		if (!possibleSocket.IsStarted)
		{
			throw new Exception($"Couldn't connect to {uri}");
		}

		_socket = possibleSocket;

		try
		{
			SetupSocket(_socket);
		}
		catch (Exception e)
		{
			throw new Exception($"Couldn't connect to {uri}", innerException: e);
		}

		return _socket;
	}

	/// <summary>
	/// Disconnects from PPSSPP, cleans up pending tickets and triggers the OnClose event.
	/// </summary>
	/// <exception cref="Exception"></exception>
	public async Task DisconnectAsync()
	{
		if (_socket == null)
		{
			throw new Exception("Not Connected");
		}
		
		FailAllPending("Disconnected from PPSSPP");
		
		await _socket.Stop(WebSocketCloseStatus.NormalClosure, "Disconnected from PPSSPP");
		_socket.Dispose();
		_socket = null;
		
		OnClose?.Invoke(this, EventArgs.Empty);
	}
	
	internal Task<T> SendAsync<T>(ResultMessage data)
		where T : MessageEventArgs, new()
	{
		if (_socket == null)
		{
			throw new Exception("Not connected");
		}

		if (_noResponseEvents.Contains(data.Event))
		{
			_socket.Send(JsonSerializer.Serialize(data, new JsonSerializerOptions
			{
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			}));
		}

		var ticket = MakeTicket();
		
		var tcs = new TaskCompletionSource<T>();
		
		_pendingTickets[ticket] = (sender, args) =>
		{
			if (args.GetProperty("event").GetString() == "error")
			{
				tcs.SetException(new Exception(args.GetProperty("message").GetString())
				{
					Data =
					{
						["ReceivedMessage"] = args.GetRawText(),
					},
				});
			}
			else
			{
				var result = args.Deserialize<T>() ?? new T();
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
						await HandleErrorAsync(root.GetProperty("message").GetString() ?? "Unknown Error", (ErrorLevels)root.GetProperty("level").GetByte());
					}

					var handled = false;
					if (root.TryGetProperty("ticket", out var ticket))
					{
						if (!string.IsNullOrWhiteSpace(ticket.GetString()) && _pendingTickets.TryGetValue(ticket.GetString()!, out var handler))
						{
							_pendingTickets.Remove(ticket.GetString()!);

							handler.Invoke(this, root);
							handled = true;
						}

						if (!handled)
						{
							await HandleErrorAsync("Received mismatched ticket: " + ticket.GetString(), ErrorLevels.Error);
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
								Game.Resumed(root);
								break;
							case "game.pause":
								Game.Paused(root);
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
					await HandleErrorAsync($"Failed to parse message from PPSSPP: {ex.Message}", ErrorLevels.Error);
					
					//if(ex.Data != null && ex.Data.Contains("ReceivedMessage") && ex.Data["ReceivedMessage"] is JsonDocument)
                    //		Debug.WriteLine(((JsonDocument)ex.Data["ReceivedMessage"]).RootElement.GetRawText());
					
					Debug.WriteLine(root.GetRawText());
					throw;
				}
			}))
			.Merge()
			.Subscribe();
	}

	private async Task<WebsocketClient> TryNextEndpointAsync(IEnumerable<Endpoint>? listing)
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

			var socket = await ConnectAsync(endpoint);

			if (socket.IsRunning)
			{
				return socket;
			}

			if (endpoints.Length > 1)
			{
				listing = endpoints.Skip(1);
				continue;
			}

			break;
		}

		return default!;
	}

	private async Task HandleErrorAsync(string message, ErrorLevels level = ErrorLevels.Unknown)
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
			await Console.Error.WriteLineAsync($"{level}: {message}");
		}
		else
		{
			Console.WriteLine($"{level}: {message}");
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

	/// <summary>
	/// Once this object is disposed the connection is cleaned up with events triggered
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (_socket != null && (_socket.IsStarted || _socket.IsRunning))
		{
			await DisconnectAsync();
		}

		GC.SuppressFinalize(this);
	}
}

public class AlreadyConnectedException : Exception
{
	/// <inheritdoc cref="AlreadyConnectedException"/>
	public AlreadyConnectedException()
	{
		throw new Exception("Already connected, disconnect first");
	}
}


