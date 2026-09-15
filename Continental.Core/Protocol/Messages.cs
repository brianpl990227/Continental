using System.Text.Json;
using System.Text.Json.Serialization;
using Continental.Core.Bots;
using Continental.Core.Rules;

namespace Continental.Core.Protocol;

public static class MessageType
{

    public const string Join = "join";
    public const string SetOptions = "options";
    public const string AddBot = "addbot";
    public const string Kick = "kick";
    public const string Start = "start";
    public const string Draw = "draw";
    public const string ClaimSteal = "steal";
    public const string LayDown = "laydown";
    public const string Extend = "extend";
    public const string SwapJoker = "swapjoker";
    public const string Discard = "discard";
    public const string NextRound = "nextround";
    public const string PlayAgain = "playagain";
    public const string Leave = "leave";

    public const string Ping = "ping";

    public const string Welcome = "welcome";
    public const string State = "state";
    public const string Error = "error";
    public const string Pong = "pong";
}

public sealed record MeldSpecDto(int Kind, List<int> CardIds);

public sealed class ClientMessage
{
    public string Type { get; set; } = "";

    public string? Name { get; set; }

    public string? PlayerId { get; set; }

    public string? MeldId { get; set; }

    public int? CardId { get; set; }

    public int? Source { get; set; }

    public int? Position { get; set; }

    public string? TargetMeldId { get; set; }

    public int? BotLevel { get; set; }

    public List<MeldSpecDto>? Melds { get; set; }

    public GameOptions? Options { get; set; }
}

public sealed class ServerMessage
{
    public string Type { get; set; } = "";

    public string? YouId { get; set; }

    public string? Error { get; set; }

    public PlayerView? View { get; set; }
}

public sealed class RoomAnnounce
{
    public string RoomId { get; set; } = "";

    public string RoomName { get; set; } = "";

    public string HostName { get; set; } = "";

    public string Address { get; set; } = "";

    public int Port { get; set; }

    public int Players { get; set; }

    public int MaxPlayers { get; set; } = 6;

    public bool InProgress { get; set; }

    public string Ruleset { get; set; } = "";

    public int StartingCards { get; set; }

    [JsonIgnore]
    public DateTimeOffset SeenAt { get; set; } = DateTimeOffset.UtcNow;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ClientMessage))]
[JsonSerializable(typeof(ServerMessage))]
[JsonSerializable(typeof(RoomAnnounce))]
[JsonSerializable(typeof(PlayerView))]
[JsonSerializable(typeof(GameOptions))]
public partial class ProtocolJsonContext : JsonSerializerContext;

public static class Wire
{
    public static string Serialize(ClientMessage message)
        => JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ClientMessage);

    public static string Serialize(ServerMessage message)
        => JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ServerMessage);

    public static string Serialize(RoomAnnounce announce)
        => JsonSerializer.Serialize(announce, ProtocolJsonContext.Default.RoomAnnounce);

    public static ClientMessage? ReadClient(string json)
        => JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.ClientMessage);

    public static ServerMessage? ReadServer(string json)
        => JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.ServerMessage);

    public static RoomAnnounce? ReadAnnounce(string json)
        => JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.RoomAnnounce);
}
