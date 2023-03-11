using System.Diagnostics;
using ppsspp_api;

var ppsspp = new PPSSPP("ppsspp-api-samples", "1.0.0");

try
{
	//await ppsspp.AutoConnect();
	ppsspp.Connect(new Uri("ws://127.0.0.1:45333/debugger"));

	var result = await ppsspp.Game.Version();

	Console.WriteLine($"Connected to {result.Name} version {result.Version}");

	var gameResult = await ppsspp.Game.Status();

	if (gameResult.Game == null)
	{
		Console.WriteLine("You aren't playing any game yet? Boring.");
		return;
	}

	Console.WriteLine($"Playing {gameResult.Game.Title} ({gameResult.Game.Id} version {gameResult.Game.Version})");
}
catch (Exception error)
{
	await Console.Error.WriteLineAsync($"Something went wrong: {error.Message}");
	Debug.WriteLine(error.Data["ReceivedMessage"]);
}
finally
{
	ppsspp.Disconnect();
}