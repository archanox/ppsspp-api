using System.Text.Json;
using System.Text.Json.Serialization;

namespace ppsspp_api;

public class GameResultArgs : EventArgs
{
	[JsonPropertyName("title")]
	public string Title { get; set; }
	
	[JsonPropertyName("id")]
	public string Id { get; set; }
	
	[JsonPropertyName("version")]
	public string Version { get; set; }
}

public record Endpoint(
	[property: JsonPropertyName("ip")] string IpAddress,
	[property: JsonPropertyName("p")] int Port,
	[property: JsonPropertyName("t")] int Time
);

public class ResultMessage : IMessage
{
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("version")]
	public string Version { get; set; }
	
	[property: JsonPropertyName("level")]
	public PPSSPP.ErrorLevels Level { get; set; }

	[JsonPropertyName("address")]
	public uint? Address { get; set; }

	[JsonPropertyName("size")]
	public uint? Size { get; set; }

	[JsonPropertyName("break")]
	public bool? Break { get; set; }

	[JsonPropertyName("enabled")]
	public bool? Enabled { get; set; }
}

public class IMessage : EventArgs
{
	[JsonPropertyName("event")]
	public string Event { get; set; }
	
	[JsonPropertyName("message")]
	public string Message { get; set; }
	
	[JsonPropertyName("ticket")]
	public string Ticket { get; set; }

	[JsonIgnore]
	public JsonElement Data { get; set; }
}

public class MemoryReadResult : IMessage
{
	[JsonPropertyName("base64")]
	public string Base64 { get; set; }
	
	[JsonIgnore]
	public byte[] ByteArray => Convert.FromBase64String(Base64);
}

public class GameStatusResult : IMessage
{
	[JsonPropertyName("game")]
	public GameResultArgs? Game { get; set; }
	
	[JsonPropertyName("paused")]
	public bool Paused { get; set; }
}

public class VersionResult : IMessage
{
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("version")]
	public string Version { get; set; }
}

public class HleFuncListResult : IMessage
{
	[JsonPropertyName("functions")]
	public Function[] Functions { get; set; }
}

public class Function
{
	[JsonPropertyName("name")]
	public string Name { get; set; }
	
	[JsonPropertyName("address")]
	public uint Address { get; set; }
	
	[JsonPropertyName("size")]
	public int Size { get; set; }
}

public class CpuBreakpointAddResult : IMessage
{
}

public class CpuSteppingResult : IMessage
{
	[JsonPropertyName("pc")]
	public int Pc { get; set; }
	
	[JsonPropertyName("ticks")]
    public ulong Ticks { get; set; }
    
    [JsonPropertyName("reason")]
    public string Reason { get; set; }
    
    [JsonPropertyName("relatedAddress")]
    public int RelatedAddress { get; set; }
}

public class CpuGetReg: IMessage
{
	[JsonPropertyName("uintValue")]
	public uint? UIntValue { get; set; }
	
	[JsonPropertyName("category")]
	public int Category { get; set; }
	
	[JsonPropertyName("register")]
	public int Register { get; set; }
	
	[JsonPropertyName("floatValue")]
	public string FloatValue { get; set; }
}

public class MemoryReadStringResult: IMessage
{
	[JsonPropertyName("value")]
	public string Value { get; set; }
}

public class CpuResumeResult: IMessage
{
}

public class InputAnalogResult : IMessage
{
	[JsonPropertyName("stick")]
	public string Stick { get; set; }
	
	[JsonPropertyName("x")]
	public int X { get; set; }
	
	[JsonPropertyName("y")]
	public int Y { get; set; }
}


public class InputButtonResult : IMessage
{
	[JsonPropertyName("buttons")]
	public Buttons Buttons { get; set; }
	
	[JsonPropertyName("changed")]
	public Buttons Changed { get; set; }
}

public class Buttons
{
	[JsonPropertyName("playpause")]
	public bool PlayPause { get; set; }
	
	[JsonPropertyName("forward")]
	public bool Forward { get; set; }
	
	[JsonPropertyName("vol_up")]
	public bool VolumeUp { get; set; }
	
	[JsonPropertyName("select")]
	public bool Select { get; set; }
	
	[JsonPropertyName("remote_hold")]
	public bool RemoteHold { get; set; }
	
	[JsonPropertyName("back")]
	public bool Back { get; set; }
	
	[JsonPropertyName("rtrigger")]
	public bool RightTrigger { get; set; }
	
	[JsonPropertyName("ltrigger")]
	public bool LeftTrigger { get; set; }
	
	[JsonPropertyName("memstick")]
	public bool MemoryStick { get; set; }
	
	[JsonPropertyName("triangle")]
	public bool Triangle { get; set; }
	
	[JsonPropertyName("screen")]
	public bool Screen { get; set; }
	
	[JsonPropertyName("circle")]
	public bool Circle { get; set; }
	
	[JsonPropertyName("start")]
	public bool Start { get; set; }
	
	[JsonPropertyName("disc")]
	public bool Disc { get; set; }
	
	[JsonPropertyName("right")]
	public bool Right { get; set; }
	
	[JsonPropertyName("hold")]
	public bool Hold { get; set; }
	
	[JsonPropertyName("left")]
	public bool Left { get; set; }
	
	[JsonPropertyName("home")]
	public bool Home { get; set; }
	
	[JsonPropertyName("vol_down")]
	public bool VolumeDown { get; set; }
	
	[JsonPropertyName("down")]
	public bool Down { get; set; }
	
	[JsonPropertyName("wlan")]
	public bool Wlan { get; set; }
	
	[JsonPropertyName("up")]
	public bool Up { get; set; }
	
	[JsonPropertyName("note")]
	public bool Note { get; set; }
	
	[JsonPropertyName("square")]
	public bool Square { get; set; }
	
	[JsonPropertyName("cross")]
	public bool Cross { get; set; }
}