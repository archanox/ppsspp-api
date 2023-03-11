using System.Text.Json;

namespace ppsspp_api.Endpoints;

public sealed class Hle : Endpoint
{
	internal Hle(Ppsspp ppsspp) : base(ppsspp)
	{
	}

	public async Task<HleFuncListResult> FunctionListAsync()
	{
		return await _ppsspp.SendAsync<HleFuncListResult>(new ResultMessage
		{
			Event = "hle.func.list",
		});
	}
}