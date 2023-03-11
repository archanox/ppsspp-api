namespace ppsspp_api.Endpoints;

public sealed class Memory : Endpoint
{
	internal Memory(Ppsspp ppsspp) : base(ppsspp)
	{
	}
	
	public async Task<MemoryReadResult> ReadAsync(uint address, uint size)
	{
		return await _ppsspp.SendAsync<MemoryReadResult>(new ResultMessage
		{
			Event = "memory.read",
			Address = address,
			Size = size,
		});
	}

	public async Task<MemoryReadStringResult> ReadStringAsync(uint uIntValue)
	{
		try
		{
			return await _ppsspp.SendAsync<MemoryReadStringResult>(new ResultMessage
			{
				Event = "memory.readString",
				Address = uIntValue,
			});
		}
		catch (Exception e)
		{
			await Console.Error.WriteLineAsync(e.Message);
			return new MemoryReadStringResult();
		}
		
	}
}