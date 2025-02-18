using System.Text.Json;
using Microsoft.Win32;

namespace ppsspp_api.Endpoints;

public sealed class Cpu : Endpoint
{
	public AsyncEvent<CpuSteppingResult> OnStep = new();
	public AsyncEvent<CpuResumeResult> OnResume = new();

	internal Cpu(Ppsspp ppsspp) : base(ppsspp)
	{
	}

	public async ValueTask<CpuResumeResult> ResumeAsync()
	{
		return await _ppsspp.SendAsync<CpuResumeResult>(new ResultMessage
		{
			Event = "cpu.resume",
		});
	}
	
	public async ValueTask<CpuGetReg> GetRegisterAsync(string register)
	{
        ArgumentException.ThrowIfNullOrEmpty(register);
		return await _ppsspp.SendAsync<CpuGetReg>(new ResultMessage
		{
			Event = "cpu.getReg",
			Name = register,
		});
	}

	public async Task AddBreakpointAsync(uint funcAddress, bool? enabled = null)
    {
		await _ppsspp.SendAsync<MessageEventArgs>(new ResultMessage
		{
			Event = "cpu.breakpoint.add",
			Address = funcAddress,
			Enabled = enabled,
		});
	}
	
	public async Task RemoveBreakpointAsync(uint funcAddress)
	{
		await _ppsspp.SendAsync<MessageEventArgs>(new ResultMessage
		{
			Event = "cpu.breakpoint.remove",
			Address = funcAddress,
		});
	}
}