using System.Text.Json;

namespace ppsspp_api.Endpoints;

public sealed class Input : Endpoint
{
	internal Input(PPSSPP ppsspp) : base(ppsspp)
	{
	}

	public event EventHandler<InputButtonResult>? OnButtonChange;
	public event EventHandler<InputAnalogResult>? OnAnalogChange;
	
	public void ButtonChanged(JsonElement root)
	{
		OnButtonChange?.Invoke(this, root.Deserialize<InputButtonResult>() ?? new InputButtonResult());
	}

	public void AnalogChanged(JsonElement root)
	{
		OnAnalogChange?.Invoke(this, root.Deserialize<InputAnalogResult>() ?? new InputAnalogResult());
	}
}



