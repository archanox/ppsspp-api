using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ppsspp_api.Endpoints;
using Websocket.Client;
namespace ppsspp_api;

/// <summary>
/// 
/// </summary>
public sealed class Ppsspp : IAsyncDisposable, IDisposable
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
	internal string ClientName { get; init; }

	/// <summary>
	/// string indicating version of app or tool
	/// </summary>
	internal string ClientVersion { get; init; }

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
	public AsyncEvent<ResultMessage>? OnError;

	/// <summary>
	/// Set this to a function with no parameters called on disconnect.
	/// </summary>
	public AsyncEvent<EventArgs>? OnClose;

	private WebsocketClient? _socket;
	private bool disposedValue;
	private readonly ConcurrentDictionary<string, AsyncEvent<JsonElement>> _pendingTickets = new();

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
	/// <exception cref="FailedConnectionException">Throws when client is unable to connect to a nearby instance</exception>
	/// <exception cref="AlreadyConnectedException">Throws when the socket already exists</exception>
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
			throw new FailedConnectionException(_socket.Url, innerException: e);
		}
	}

	/// <summary>
	/// Connect to a specific WebSocket URI (ie. ws://127.0.0.1:45333/debugger)
	/// </summary>
	/// <param name="uri">The PPSSPP debugger endpoint</param>
	/// <returns>A subscribed <see cref="WebsocketClient"/> with a subprotocol and listeners</returns>
	/// <exception cref="AlreadyConnectedException">Throws when client is already connected to the debugger</exception>
	/// <exception cref="FailedConnectionException">Throws when client is unable to connect to <paramref name="uri"/></exception>
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
			throw new FailedConnectionException(uri);
		}

		_socket = possibleSocket;

		try
		{
			SetupSocket(_socket);
		}
		catch (Exception e)
		{
			throw new FailedConnectionException(uri, innerException: e);
		}

		return _socket;
	}

	internal async Task<T> SendAsync<T>(ResultMessage data)
		where T : MessageEventArgs, new()
	{
		ArgumentNullException.ThrowIfNull(data);

		if (_socket == null)
		{
			throw new NotConnectedException();
		}

		if (_noResponseEvents.Contains(data.Event))
		{
			await _socket.SendInstant(JsonSerializer.Serialize(data, new JsonSerializerOptions
			{
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			}));
		}

		var ticket = MakeTicket(_pendingTickets);

		var tcs = new TaskCompletionSource<T>();


		AddTicket(ticket, tcs);

		data.Ticket = ticket;

		await _socket.SendInstant(JsonSerializer.Serialize(data, new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		}));


		return await tcs.Task;
	}

	private void AddTicket<T>(string ticket, TaskCompletionSource<T> tcs) where T : MessageEventArgs, new()
	{
		async Task Callback(object sender, JsonElement args)
		{
			if (args.GetProperty("event").GetString() == "error")
			{
				tcs.SetException(new Exception(args.GetProperty("message").GetString()) { Data = { ["ReceivedMessage"] = args.GetRawText() }, Source = sender.ToString()});
			}
			else
			{
				var result = args.Deserialize<T>() ?? new T();
				result.Data = args;
				tcs.SetResult(result);
			}

			await Task.CompletedTask;
		}

		_pendingTickets.TryAdd(ticket, new AsyncEvent<JsonElement>(Callback));
	}

	private void SetupSocket(IWebsocketClient? websocketClient)
	{
		websocketClient?.DisconnectionHappened.Subscribe(info =>
			{
				OnClose?.InvokeAsync(this, EventArgs.Empty).Wait();

				FailAllPendingAsync($"PPSSPP disconnected {info.Exception?.Message}").Wait();
			}
		);

		websocketClient?.MessageReceived
			.Select(x => JsonDocument.Parse(x.Text).RootElement)
			.Where(x => x.GetProperty("event").GetString() != "error")
			.Where(x => !x.TryGetProperty("ticket", out _))
			.Select(root => Observable.FromAsync(async () =>
			{
				try
				{
					var eventName = root.GetProperty("event").GetString();
					switch (eventName)
					{
						case "game.start":
							await AddEventResultsAsync(root, Game.OnStarted);
							break;
						case "game.quit":
							await AddEventResultsAsync(root, Game.OnQuit);
							break;
						case "game.resume":
							await AddEventResultsAsync(root, Game.OnResumed);
							break;
						case "game.pause":
							await AddEventResultsAsync(root, Game.OnPaused);
							break;
						case "cpu.stepping":
							await AddEventResultsAsync(root, Cpu.OnStep);
							break;
						case "input.buttons":
							await AddEventResultsAsync(root, Input.OnButtonChange);
							break;
						case "input.analog":
							await AddEventResultsAsync(root, Input.OnAnalogChange);
							break;
						case "cpu.resume":
							await AddEventResultsAsync(root, Cpu.OnResume);
							break;
						case "log":
							break;
						default:
							await Console.Error.WriteLineAsync($"{eventName} is unsupported");
							Debug.WriteLine(root.GetRawText());
							break;
					}
				}
				catch (Exception ex)
				{
					await HandleErrorAsync($"Failed to parse message from PPSSPP: {ex.Message}", ErrorLevels.Error);

					ex.Data["ReceivedMessage"] = root.GetRawText();

					await Console.Error.WriteLineAsync(root.GetRawText());
					//throw;
				}
			}))
			//.Merge()
			.Concat()
			.ObserveOn(TaskPoolScheduler.Default)
			.Subscribe();

		websocketClient?.MessageReceived
			.Select(x => JsonDocument.Parse(x.Text).RootElement)
			.Where(x=>x.GetProperty("event").GetString() != "error")
			.Where(x => x.TryGetProperty("ticket", out _))
			.Select(root => Observable.FromAsync(async () =>
			{
				try
				{
					var handled = false;
					var ticket = root.GetProperty("ticket").GetString();

					if (!string.IsNullOrWhiteSpace(ticket) && _pendingTickets.TryRemove(ticket, out var handler))
					{
						await handler.InvokeAsync(this, root);
						
						handled = true;
					}

					if (!handled)
					{
						await HandleErrorAsync("Received mismatched ticket: " + ticket, ErrorLevels.Error);
					}
				}
				catch (Exception ex)
				{
					await HandleErrorAsync($"Failed to parse ticket from PPSSPP: {ex.Message}", ErrorLevels.Error);

					ex.Data["ReceivedMessage"] = root.GetRawText();

					await Console.Error.WriteLineAsync(root.GetRawText());
					//throw;
				}
			}))
			.Concat()
			//.Merge()
			.ObserveOn(TaskPoolScheduler.Default)
			.Subscribe();

		websocketClient?.MessageReceived
			.Select(x => JsonDocument.Parse(x.Text).RootElement)
			.Where(x => x.GetProperty("event").GetString() == "error")
			.Select(message => Observable.FromAsync(async () =>
		{
			try
			{
				await HandleErrorAsync(message.GetProperty("message").GetString() ?? "Unknown Error", (ErrorLevels)message.GetProperty("level").GetByte());
			}
			catch (Exception ex)
			{
				await HandleErrorAsync($"Failed to parse error from PPSSPP: {ex.Message}", ErrorLevels.Error);

				ex.Data["ReceivedMessage"] = message.GetRawText();

				await Console.Error.WriteLineAsync(message.GetRawText());
				//throw;
			}
		}))
			.Concat()
			//.Merge()
			.ObserveOn(TaskPoolScheduler.Default)
			.Subscribe();
	}

	private async Task<WebsocketClient> TryNextEndpointAsync(IEnumerable<Endpoint>? listing)
	{
		while (true)
		{
			var endpoints = listing as Endpoint[] ?? listing?.ToArray();
			if (endpoints == null || !endpoints.Any())
			{
				throw new NoEndpointsException();
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
				Debug.WriteLine(endpoint);
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
			await (OnError?.InvokeAsync(this, new ResultMessage
			{
				Message = message,
				Level = level,
			}) ?? Task.CompletedTask);
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

	private static readonly ThreadLocal<Random> Random = new (() => new Random());

	private static string MakeTicket(IReadOnlyDictionary<string, AsyncEvent<JsonElement>> pendingTickets)
	{
		const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
		
		while (true)
		{
			var ticket = new string(Enumerable.Repeat(chars, 11)
				.Select(s => s[Random.Value!.Next(s.Length)])
				.ToArray());

			if (pendingTickets.ContainsKey(ticket))
			{
				continue;
			}

			return ticket;
		}
	}

	private async Task FailAllPendingAsync(string message)
	{
		var data = new ResultMessage { Event = "error", Message = message, Level = ErrorLevels.Error };

		foreach (var pendingTicket in _pendingTickets.ToArray())
		{
			await _pendingTickets[pendingTicket.Key].InvokeAsync(this, JsonSerializer.SerializeToElement(data));
		}

		_pendingTickets.Clear();
	}

	/// <summary>
	/// Once this object is disposed asynchronously the connection is cleaned up with events triggered
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (_socket != null && (_socket.IsStarted || _socket.IsRunning))
		{
			await _socket.StopOrFail(WebSocketCloseStatus.NormalClosure, "Disconnected from PPSSPP");
		}
		await FailAllPendingAsync("Disconnected from PPSSPP");
		if (!disposedValue)
		{
			// TODO: dispose managed state (managed objects)
			_socket?.Dispose();

			// TODO: free unmanaged resources (unmanaged objects) and override finalizer
			// TODO: set large fields to null
			_socket = null;
			disposedValue = true;
		}
		await (OnClose?.InvokeAsync(this, EventArgs.Empty) ?? Task.CompletedTask);
		//GC.SuppressFinalize(this);
	}



	/// <summary>
	/// Once this object is disposed the connection is cleaned up with events triggered
	/// </summary>
	public void Dispose()
	{
		if (_socket != null && (_socket.IsStarted || _socket.IsRunning))
		{
			_socket.StopOrFail(WebSocketCloseStatus.NormalClosure, "Disconnected from PPSSPP").RunSynchronously();
		}
		FailAllPendingAsync("Disconnected from PPSSPP").RunSynchronously();
		if (!disposedValue)
		{
			// TODO: dispose managed state (managed objects)
			_socket?.Dispose();

			// TODO: free unmanaged resources (unmanaged objects) and override finalizer
			// TODO: set large fields to null
			_socket = null;
			disposedValue = true;
		}
		OnClose?.InvokeAsync(this, EventArgs.Empty).RunSynchronously();
		//GC.SuppressFinalize(this);
	}

	internal async Task AddEventResultsAsync<TMessageEvent>(JsonElement root, AsyncEvent<TMessageEvent> asyncEvent)
		where TMessageEvent : MessageEventArgs, new()
	{
		if(asyncEvent.GetInvocationList().Any())
			await (asyncEvent?.InvokeAsync(this, root.Deserialize<TMessageEvent>() ?? new TMessageEvent()) ?? Task.CompletedTask);
	}
}

/// <summary>
/// 
/// </summary>
[Serializable]
public class FailedConnectionException : Exception
{

	public FailedConnectionException() : base()
	{
	}

	public FailedConnectionException(string? message) : base(message)
	{
	}

	public FailedConnectionException(Uri url) : base($"Couldn't connect to {url}")
	{
	}

	public FailedConnectionException(Uri url, Exception? innerException) : base($"Couldn't connect to {url}", innerException)
	{
	}

	public FailedConnectionException(string? message, Exception? innerException) : base(message, innerException)
	{
	}

	protected FailedConnectionException(SerializationInfo info, StreamingContext context) : base(info, context)
	{
	}
}

/// <summary>
/// 
/// </summary>
[Serializable]
public class NotConnectedException : Exception
{
	public NotConnectedException() : base("Not connected")
	{
	}

	public NotConnectedException(string? message) : base(message)
	{
	}

	public NotConnectedException(string? message, Exception? innerException) : base(message, innerException)
	{
	}

	protected NotConnectedException(SerializationInfo info, StreamingContext context) : base(info, context)
	{
	}
}

/// <summary>
/// 
/// </summary>
[Serializable]
public class NoEndpointsException : Exception
{
	public NoEndpointsException() : base("Couldn't connect automatically. Is PPSSPP connected to the same network?")
	{
	}

	public NoEndpointsException(string? message) : base(message)
	{
	}

	public NoEndpointsException(string? message, Exception? innerException) : base(message, innerException)
	{
	}

	protected NoEndpointsException(SerializationInfo info, StreamingContext context) : base(info, context)
	{
	}
}

/// <summary>
/// 
/// </summary>
[Serializable]
public class AlreadyConnectedException : Exception
{
	/// <inheritdoc cref="AlreadyConnectedException"/>
	public AlreadyConnectedException() : base("Already connected, disconnect first")
	{
	}

	protected AlreadyConnectedException(SerializationInfo serializationInfo, StreamingContext streamingContext) : base(serializationInfo, streamingContext)
	{
	}
}


public class AsyncEvent<TEventArgs> //where TEventArgs : EventArgs
{
	private readonly List<Func<object, TEventArgs, Task>> _invocationList;
	private readonly object _locker;

	public AsyncEvent()
	{
		_invocationList = new List<Func<object, TEventArgs, Task>>();
		_locker = new object();
	}

	public AsyncEvent(Func<object, TEventArgs, Task> invocationList)
	{
		_invocationList = new List<Func<object, TEventArgs, Task>>() { invocationList };
		_locker = new object();
	}

	public void Add(Func<object, TEventArgs, Task> callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		lock (this._locker)
		{
			this._invocationList.Add(callback);
		}
	}

	public void Remove(Func<object, TEventArgs, Task> callback)
	{
		ArgumentNullException.ThrowIfNull(callback);

		lock (this._locker)
		{
			this._invocationList.Remove(callback);
		}
	}

	public async Task InvokeAsync(object sender, TEventArgs eventArgs)
	{
		List<Func<object, TEventArgs, Task>> tmpInvocationList;
		lock (_locker)
		{
			tmpInvocationList = new List<Func<object, TEventArgs, Task>>(_invocationList);
		}

		foreach (var callback in tmpInvocationList)
		{
			//Assuming we want a serial invocation, for a parallel invocation we can use Task.WhenAll instead
			await callback(sender, eventArgs);
		}
	}

	internal List<Func<object, TEventArgs, Task>> GetInvocationList()
	{
		lock (_locker)
		{
			return _invocationList;
		}
	}
}



public class LogResult
{
	public string _event { get; set; }
	public string timestamp { get; set; }
	public string header { get; set; }
	public string message { get; set; }
	public Ppsspp.ErrorLevels level { get; set; }
	public string channel { get; set; }
}
