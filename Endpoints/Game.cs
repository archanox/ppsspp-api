using System.Text.Json;

namespace ppsspp_api.Endpoints;

public sealed class Game : Endpoint
{
	internal Game(PPSSPP ppsspp) : base(ppsspp)
	{
	}
	
	public event EventHandler<GameResultArgs>? OnStarted;
	public event EventHandler<GameResultArgs>? OnQuit;
	public event EventHandler<GameResultArgs>? OnPauseChange;

	/// <summary>
	/// Reset emulation (game.reset)
	/// Use this if you need to break on start and do something before the game starts.
	/// Response (same event name) with no extra data or error.
	/// </summary>
	/// <param name="breakOnStart">optional boolean, true to break CPU on start.  Use cpu.resume afterward.</param>
	public async Task Reset(bool? breakOnStart = null)
	{
		await _ppsspp.Send<IMessage>(new ResultMessage
		{
			Event = "game.reset",
			Break = breakOnStart,
		});
	}

	public async Task<GameStatusResult> Status()
	{
		return await _ppsspp.Send<GameStatusResult>(new ResultMessage
		{
			Event = "game.status",
		});
	}

	/// <summary>
	/// Returns the version of PPSSPP that you are connecting to
	/// </summary>
	/// <returns></returns>
	public async Task<VersionResult> Version()
	{
		return await _ppsspp.Send<VersionResult>(new ResultMessage
		{
			Event = "version",
			Name = _ppsspp.ClientName,
			Version = _ppsspp.ClientVersion,
		});
	}

	public void Started(JsonElement root)
	{
		OnStarted?.Invoke(this, root.Deserialize<GameResultArgs>() ?? new GameResultArgs());
	}

	public void Quit(JsonElement root)
	{
		OnQuit?.Invoke(this, root.Deserialize<GameResultArgs>() ?? new GameResultArgs());
	}

	public void PauseChanged(JsonElement root)
	{
		OnPauseChange?.Invoke(this, root.Deserialize<GameResultArgs>() ?? new GameResultArgs());
	}
}