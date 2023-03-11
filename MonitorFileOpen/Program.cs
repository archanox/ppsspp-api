using System.Diagnostics;
using System.Text.Json;
using ppsspp_api;

var ppsspp = new PPSSPP("ppsspp-api-samples", "1.0.0");

uint? functionAddress = null;

try
{
	//await ppsspp.AutoConnect();
	ppsspp.Connect(new Uri("ws://192.168.1.8:45333/debugger"));

	var result = await ppsspp.Game.Version();

	Console.WriteLine($"Connected to {result.Name} version {result.Version}");

	var result2 = await ppsspp.Hle.FunctionList();
	
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

	ppsspp.Cpu.OnStep += (_, cpuSteppingResult) => CpuOnStep(cpuSteppingResult, functionAddress.Value);
	
	await ppsspp.Cpu.AddBreakpoint(functionAddress.Value);
	
	await Task.Delay(-1);
}
catch (Exception ex)
{
	await Console.Error.WriteLineAsync($"Something went wrong: {ex.Message}");
	
	if(ex.Data.Contains("ReceivedMessage") && ex.Data["ReceivedMessage"] is JsonDocument)
		Debug.WriteLine(((JsonDocument)ex.Data["ReceivedMessage"]).RootElement.GetRawText());
}
finally
{
	if(functionAddress.HasValue)
		await ppsspp.Cpu.RemoveBreakpoint(functionAddress.Value);
	
	ppsspp.Disconnect();
}

async void CpuOnStep(CpuSteppingResult cpuSteppingResult, uint address)
{
	if (cpuSteppingResult.Pc != address)
	{
		return;
	}

	var cpuGetRegResult = await ppsspp.Cpu.GetRegister("a0");

	var stringResult = await ppsspp.Memory.ReadString(cpuGetRegResult.UIntValue.Value);

	try
	{
		Console.WriteLine($"Opening {stringResult.Value}...");
		var resumeResult = await ppsspp.Cpu.Resume();
		Console.WriteLine(resumeResult.Data.GetRawText());
	}
	catch (Exception ex)
	{
		Console.Error.WriteLine($"Failed to log open? {ex.Message}");
		
		if(ex.Data.Contains("ReceivedMessage") && ex.Data["ReceivedMessage"] is JsonDocument)
			Debug.WriteLine(((JsonDocument)ex.Data["ReceivedMessage"]).RootElement.GetRawText());
	}
}







