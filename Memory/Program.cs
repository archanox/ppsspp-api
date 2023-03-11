using System.Diagnostics;
using ppsspp_api;

await using var ppsspp = new Ppsspp("ppsspp-api-samples", "1.0.0");

try
{
	// await ppsspp.AutoConnectAsync();
	await ppsspp.ConnectAsync(new Uri("ws://127.0.0.1:45333/debugger"));

	var result = await ppsspp.Game.VersionAsync();

	Console.WriteLine($"Connected to {result.Name} version {result.Version}");

	var memory = await ppsspp.Memory.ReadAsync(0x08000000, 1024);

	Console.WriteLine(memory.ByteArray.Length);
}
catch (Exception error)
{
	await Console.Error.WriteLineAsync($"Something went wrong: {error.Message}");
	Debug.WriteLine(error.Data["ReceivedMessage"]);
}