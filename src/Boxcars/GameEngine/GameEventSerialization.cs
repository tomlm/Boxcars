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

    public static PlayerAction? DeserializePlayerAction(string? eventKind, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return NormalizeEventKind(eventKind) switch
            {
                "ChooseHomeCity" => JsonSerializer.Deserialize<ChooseHomeCityAction>(payload),
                "ResolveHomeSwap" => JsonSerializer.Deserialize<ResolveHomeSwapAction>(payload),
                "PickDestination" => JsonSerializer.Deserialize<PickDestinationAction>(payload),
                "Declare" => JsonSerializer.Deserialize<DeclareAction>(payload),
                "ChooseDestinationRegion" => JsonSerializer.Deserialize<ChooseDestinationRegionAction>(payload),
                "RollDice" => JsonSerializer.Deserialize<RollDiceAction>(payload),
                "ChooseRoute" => JsonSerializer.Deserialize<ChooseRouteAction>(payload),
                "Move" => JsonSerializer.Deserialize<MoveAction>(payload),
                "PurchaseRailroad" => JsonSerializer.Deserialize<PurchaseRailroadAction>(payload),
                "StartAuction" => JsonSerializer.Deserialize<StartAuctionAction>(payload),
                "Bid" => JsonSerializer.Deserialize<BidAction>(payload),
                "AuctionPass" => JsonSerializer.Deserialize<AuctionPassAction>(payload),
                "AuctionDropOut" => JsonSerializer.Deserialize<AuctionDropOutAction>(payload),
                "SellRailroad" => JsonSerializer.Deserialize<SellRailroadAction>(payload),
                "BuyEngine" => JsonSerializer.Deserialize<BuyEngineAction>(payload),
                "DeclinePurchase" => JsonSerializer.Deserialize<DeclinePurchaseAction>(payload),
                "EndTurn" => JsonSerializer.Deserialize<EndTurnAction>(payload),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeEventKind(string? eventKind)
    {
        if (string.IsNullOrWhiteSpace(eventKind))
        {
            return string.Empty;
        }

        return eventKind.EndsWith("Action", StringComparison.Ordinal)
            ? eventKind[..^"Action".Length]
            : eventKind;
    }
}
