namespace ppsspp_api.Endpoints;

public sealed class Memory : Endpoint
{
	internal Memory(PPSSPP ppsspp) : base(ppsspp)
	{
	}
	
	public async Task<MemoryReadResult> Read(uint address, uint size)
	{
		return await _ppsspp.Send<MemoryReadResult>(new ResultMessage
		{
			Event = "memory.read",
			Address = address,
			Size = size,
		});
	}

	public async Task<MemoryReadStringResult> ReadString(uint uIntValue)
	{
		try
		{
			return await _ppsspp.Send<MemoryReadStringResult>(new ResultMessage
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