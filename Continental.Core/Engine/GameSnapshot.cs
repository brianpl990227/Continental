using System.Text.Json;
using System.Text.Json.Serialization;

namespace Continental.Core.Engine;

public static class GameSnapshot
{
    public static bool IsResumable(GameState state)
        => state.Phase is GamePhase.Draw or GamePhase.StealWindow or GamePhase.Action or GamePhase.RoundEnd
           && state.Players.Count >= 2
           && state.Players.Any(p => p.IsHost);

    public static string Write(GameState state)
        => JsonSerializer.Serialize(state, SnapshotJsonContext.Default.GameState);

    public static GameState? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize(json, SnapshotJsonContext.Default.GameState);

            return state is not null && IsResumable(state) ? state : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(GameState))]
internal partial class SnapshotJsonContext : JsonSerializerContext;
