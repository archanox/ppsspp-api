using System.Text.Json;

namespace ppsspp_api.Endpoints;

public sealed class Hle : Endpoint
{
	internal Hle(PPSSPP ppsspp) : base(ppsspp)
	{
	}

	public async Task<HleFuncListResult> FunctionList()
	{
		return await _ppsspp.Send<HleFuncListResult>(new ResultMessage
		{
			Event = "hle.func.list",
		});
	}
}