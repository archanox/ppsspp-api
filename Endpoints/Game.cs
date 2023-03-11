using System.Text.Json;

namespace ppsspp_api.Endpoints;

public sealed class Game : Endpoint
{
	internal Game(Ppsspp ppsspp) : base(ppsspp)
	{
	}

	/// <summary>
	/// - game: null or an object with properties:
	///     - id: string disc ID (such as ULUS12345.)
	///     - version: string disc version.
	///     - title: string game title.
	///  - paused: boolean, true when gameplay is paused (not the same as stepping.)
	/// </summary>
	public event EventHandler<GameResultEventArgs>? OnStarted;

	/// <summary>
	/// - game: null or an object with properties:
	///     - id: string disc ID (such as ULUS12345.)
	///     - version: string disc version.
	///     - title: string game title.
	///  - paused: boolean, true when gameplay is paused (not the same as stepping.)
	/// </summary>
	public event EventHandler<GameResultEventArgs>? OnQuit;

	/// <summary>
	/// Game paused (game.pause)
	///
	/// Note: this is not the same as stepping.  This means the user went to the pause menu.
	///
	/// Sent unexpectedly with these properties:
	///  - game: null or an object with properties:
	///     - id: string disc ID (such as ULUS12345.)
	///     - version: string disc version.
	///     - title: string game title.
	/// </summary>
	public event EventHandler<GameResultEventArgs>? OnPaused;

	/// <summary>
	/// Game resumed (game.pause)
	///
	/// Note: this is not the same as stepping.  This means the user resumed from the pause menu.
	///
	/// Sent unexpectedly with these properties:
	///  - game: null or an object with properties:
	///     - id: string disc ID (such as ULUS12345.)
	///     - version: string disc version.
	///     - title: string game title.
	/// </summary>
	public event EventHandler<GameResultEventArgs>? OnResumed;

	/// <summary>
	/// Reset emulation (game.reset)
	/// Use this if you need to break on start and do something before the game starts.
	/// Response (same event name) with no extra data or error.
	/// </summary>
	/// <param name="breakOnStart">optional boolean, true to break CPU on start.  Use cpu.resume afterward.</param>
	public async Task ResetAsync(bool? breakOnStart = null)
	{
		await _ppsspp.SendAsync<MessageEventArgs>(new ResultMessage
		{
			Event = "game.reset",
			Break = breakOnStart,
		});
	}

	/// <summary>
	/// Check game status (game.status)
	/// </summary>
	/// <returns>
	/// - game: null or an object with properties:
	///     - id: string disc ID (such as ULUS12345.)
	///     - version: string disc version.
	///     - title: string game title.
	///  - paused: boolean, true when gameplay is paused (not the same as stepping.)
	/// </returns>
	public async Task<GameStatusResult> StatusAsync()
	{
		return await _ppsspp.SendAsync<GameStatusResult>(new ResultMessage
		{
			Event = "game.status",
		});
	}

	/// <summary>
	/// Returns the version of PPSSPP that you are connecting to
	/// </summary>
	/// <returns></returns>
	public async Task<VersionResult> VersionAsync()
	{
		return await _ppsspp.SendAsync<VersionResult>(new ResultMessage
		{
			Event = "version",
			Name = _ppsspp.ClientName,
			Version = _ppsspp.ClientVersion,
		});
	}

	internal void Started(JsonElement root)
	{
		OnStarted?.Invoke(this, root.Deserialize<GameResultEventArgs>() ?? new GameResultEventArgs());
	}

	internal void Quit(JsonElement root)
	{
		OnQuit?.Invoke(this, root.Deserialize<GameResultEventArgs>() ?? new GameResultEventArgs());
	}

	internal void Paused(JsonElement root)
	{
		OnPaused?.Invoke(this, root.Deserialize<GameResultEventArgs>() ?? new GameResultEventArgs());
	}

	internal void Resumed(JsonElement root)
	{
		OnResumed?.Invoke(this, root.Deserialize<GameResultEventArgs>() ?? new GameResultEventArgs());
	}
}