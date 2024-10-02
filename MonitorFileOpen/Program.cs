using System.Diagnostics;
using System.Text.Json;
using ppsspp_api;

await using var ppsspp = new Ppsspp("ppsspp-api-samples", "1.0.0");

uint? functionAddress = null;

try
{
	await ppsspp.AutoConnectAsync();
	//await ppsspp.ConnectAsync(new Uri("ws://127.0.0.1:45333/debugger"));
	//await ppsspp.ConnectAsync(new Uri("ws://127.0.0.1:52809/debugger"));
	//await ppsspp.ConnectAsync(new Uri("ws://10.211.55.3:52809/debugger")); //WindowsVM
	//await ppsspp.ConnectAsync(new Uri("ws://192.168.1.31:45333/debugger"));

	//Game_OnStartedAsync(null, new GameResultEventArgs());

	var result = await ppsspp.Game.VersionAsync();

	Console.WriteLine($"Connected to {result.Name} version {result.Version}");

	ppsspp.Game.OnStarted.Add(Game_OnStartedAsync);

    ppsspp.Game.OnQuit.Add(Game_OnQuitAsync);
	
	await Task.Delay(-1);
}
catch (Exception ex)
{
	await Console.Error.WriteLineAsync($"Something went wrong: {ex.Message}");
	
	if(ex.Data.Contains("ReceivedMessage"))
        await Console.Error.WriteLineAsync(ex.Data["ReceivedMessage"]?.ToString());
}
finally
{
	if(functionAddress.HasValue)
		await ppsspp.Cpu.RemoveBreakpointAsync(functionAddress.Value);
}

async Task Game_OnQuitAsync(object? sender, GameResultEventArgs e)
{
    if (functionAddress.HasValue)
        await ppsspp.Cpu.RemoveBreakpointAsync(functionAddress.Value);
}

async Task Game_OnStartedAsync(object? sender, GameResultEventArgs e)
{
   var result2 = await ppsspp.Hle.FunctionListAsync();

    if (!result2.Functions.Any())
    {
        throw new Exception("No functions, not playing any game?");
    }

    functionAddress = result2.Functions.SingleOrDefault(x => x.Name == "zz_sceIoOpen")?.Address;

    if (functionAddress == null)
    {
        throw new Exception("Function stub for sceIoOpen not found... game never calls it?");
    }

    Console.WriteLine($"Found func stub for sceIoOpen at 0x{functionAddress.Value:X8}");

    ppsspp.Cpu.OnStep.Add((_, cpuSteppingResult) => Cpu_OnStep(cpuSteppingResult, functionAddress.Value));

    await ppsspp.Cpu.AddBreakpointAsync(functionAddress.Value);
}

async Task Cpu_OnStep(CpuSteppingResult cpuSteppingResult, uint address)
{
	if (cpuSteppingResult.Pc != address)
	{
		return;
	}

	var cpuGetRegResult = await ppsspp.Cpu.GetRegisterAsync("a0");

	if (cpuGetRegResult.UIntValue == null) return;

	var stringResult = await ppsspp.Memory.ReadStringAsync(cpuGetRegResult.UIntValue.Value);
	try
	{
		Console.WriteLine($"Opening {stringResult.Value}...");
		var resumeResult = await ppsspp.Cpu.ResumeAsync();
		Console.WriteLine(resumeResult.Data.GetRawText());
	}
	catch (Exception ex)
	{
		Console.Error.WriteLine($"Failed to log open? {ex.Message}");

		if (ex.Data.Contains("ReceivedMessage"))
            await Console.Error.WriteLineAsync(ex.Data["ReceivedMessage"]?.ToString());
    }
}
