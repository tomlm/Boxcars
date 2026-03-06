using System.Text.Json;
using Boxcars.Engine.Persistence;

namespace Boxcars.GameEngine;

public static class GameEventSerialization
{
    public static string SerializeSnapshot(GameState snapshot)
    {
        return JsonSerializer.Serialize(snapshot);
    }

    public static GameState DeserializeSnapshot(string payload)
    {
        var snapshot = JsonSerializer.Deserialize<GameState>(payload);
        return snapshot ?? throw new InvalidOperationException("Snapshot payload could not be deserialized.");
    }

    public static string SerializeEventData(object data)
    {
        return JsonSerializer.Serialize(data);
    }
}
