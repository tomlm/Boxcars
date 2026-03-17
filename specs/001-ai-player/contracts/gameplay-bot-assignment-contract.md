# Contract: Gameplay Bot Assignment And Resolution

## Purpose

Define the in-game UI and orchestration contract for assigning a bot to a disconnected seat already under delegated control, resolving eligible phases, and stopping automation when the seat returns to normal control.

## Surface

- Player card UI in `src/Boxcars/Components/Map/PlayerBoard.razor`
- Game board page orchestration in `src/Boxcars/Components/Pages/GameBoard.razor`
- Delegated control state in `src/Boxcars/Services/GamePresenceService.cs`
- Authoritative turn execution in `src/Boxcars/GameEngine/GameEngineService.cs`

## Settings Icon Visibility Contract

The settings icon is visible only when all of the following are true:

```json
{
  "playerIsDisconnected": true,
  "delegatedControllerUserId": "current-user-id",
  "currentUserCanRelease": true
}
```

If any condition is false, the bot-assignment action is hidden.

## Assignment Dialog Contract

### Request State

```json
{
  "gameId": "game-42",
  "playerUserId": "user-disconnected",
  "controllerUserId": "user-controller",
  "availableBots": [
    {
      "botDefinitionId": "bot-iron-strategist",
      "name": "Iron Strategist"
    }
  ],
  "currentAssignment": null
}
```

### Empty State

```json
{
  "gameId": "game-42",
  "playerUserId": "user-disconnected",
  "controllerUserId": "user-controller",
  "availableBots": [],
  "emptyMessage": "No bots have been defined on the dashboard yet."
}
```

### Assign Command

```json
{
  "gameId": "game-42",
  "playerUserId": "user-disconnected",
  "controllerUserId": "user-controller",
  "botDefinitionId": "bot-iron-strategist"
}
```

### Assignment Success Output

```json
{
  "gameId": "game-42",
  "playerUserId": "user-disconnected",
  "controllerUserId": "user-controller",
  "botDefinitionId": "bot-iron-strategist",
  "status": "Active",
  "assignedUtc": "2026-03-16T18:30:00Z"
}
```

## Phase Resolution Contract

### Eligible phases

- `PickRegion`
- `Purchase`
- `Auction`
- `Sell` using deterministic built-in logic
- `Move` using existing suggested-path logic

### Decision request shape

```json
{
  "gameId": "game-42",
  "playerUserId": "user-disconnected",
  "phase": "Purchase",
  "botDefinitionId": "bot-iron-strategist",
  "botName": "Iron Strategist",
  "strategyText": "Favor high-value routes and preserve monopoly leverage.",
  "legalOptions": [
    {
      "optionId": "buy-bno",
      "optionType": "PurchaseRailroad",
      "displayText": "Buy B&O for $4000"
    },
    {
      "optionId": "no-purchase",
      "optionType": "NoPurchase",
      "displayText": "Buy nothing"
    }
  ]
}
```

### Resolution guarantees

- The server may commit only a legal action for the current authoritative state.
- If only one legal choice exists, the server commits it without OpenAI.
- If OpenAI returns an invalid, stale, or missing choice, the server commits the documented fallback for the phase.
- The resulting action is recorded in normal game history and broadcast through the existing multiplayer update path.

## Stop Conditions Contract

The assignment is cleared immediately when any of the following occurs:

- The controlling player clicks `RELEASE`.
- The disconnected player reconnects.
- The assignment is replaced with a different bot.
- The referenced bot definition no longer exists.

### Clear Output

```json
{
  "gameId": "game-42",
  "playerUserId": "user-disconnected",
  "status": "Cleared",
  "clearReason": "Reconnect"
}
```

## Behavioral Guarantees

- Assignment is impossible unless delegated control is active for a disconnected seat.
- Bot actions are attributed to the disconnected player, not the controlling player.
- Gameplay continues even when OpenAI is unavailable because the server always owns fallback behavior.
- Live edits to the selected bot definition affect future eligible phases without needing reassignment.