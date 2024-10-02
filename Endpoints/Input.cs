using System.Text.Json;

namespace ppsspp_api.Endpoints;

public sealed class Input : Endpoint
{
	internal Input(Ppsspp ppsspp) : base(ppsspp)
	{
	}

	public AsyncEvent<InputButtonResult> OnButtonChange = new();
	public AsyncEvent<InputAnalogResult> OnAnalogChange = new();
}



