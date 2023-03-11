using System.Text.Json;

namespace ppsspp_api.Endpoints;

public sealed class Cpu : Endpoint
{
	public event EventHandler<CpuSteppingResult>? OnStep;
	public event EventHandler<CpuResumeResult>? OnResume;

	internal Cpu(PPSSPP ppsspp) : base(ppsspp)
	{
	}

	public async ValueTask<CpuResumeResult> Resume()
	{
		return await _ppsspp.Send<CpuResumeResult>(new ResultMessage
		{
			Event = "cpu.resume",
		});
	}
	
	public async ValueTask<CpuGetReg> GetRegister(string register)
	{
		return await _ppsspp.Send<CpuGetReg>(new ResultMessage
		{
			Event = "cpu.getReg",
			Name = register,
		});
	}

	public async Task AddBreakpoint(uint funcAddress, bool? enabled = null)
	{
		await _ppsspp.Send<CpuBreakpointAddResult>(new ResultMessage
		{
			Event = "cpu.breakpoint.add",
			Address = funcAddress,
			Enabled = enabled,
		});
	}
	
	public async Task RemoveBreakpoint(uint funcAddress)
	{
		await _ppsspp.Send<CpuBreakpointAddResult>(new ResultMessage
		{
			Event = "cpu.breakpoint.remove",
			Address = funcAddress,
		});
	}

	public void Resumed(JsonElement root)
	{
		OnResume?.Invoke(this, root.Deserialize<CpuResumeResult>() ?? new CpuResumeResult());
	}
	
	public void Stepped(JsonElement root)
	{
		OnStep?.Invoke(this, root.Deserialize<CpuSteppingResult>() ?? new CpuSteppingResult());
	}
}