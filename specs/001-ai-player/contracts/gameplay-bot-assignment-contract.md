# Contract: Gameplay AI Seat Control And Resolution

## Purpose

Define the in-game orchestration contract for dedicated bot seats, ghost-controlled human seats, server-side AI phase resolution, and stop conditions.

## Surface

- Player card UI in `src/Boxcars/Components/Map/PlayerBoard.razor`
- Game board orchestration in `src/Boxcars/Components/Pages/GameBoard.razor`
- Presence and delegated control in `src/Boxcars/Services/GamePresenceService.cs`
- AI assignment and decision resolution in `src/Boxcars/Services/BotTurnService.cs`
- Authoritative turn execution in `src/Boxcars/GameEngine/GameEngineService.cs`

## Controller Mode Contract

```json
{
  "controllerMode": "HumanDirect | HumanDelegated | AiBotSeat | AiGhost",
  "playerUserId": "seat-user-id",
  "delegatedControllerUserId": "optional-human-controller",
  "botDefinitionId": "optional-bot-id"
}
```

### Dedicated bot seat guarantees

- Dedicated bot seats are already AI-controlled.
- Dedicated bot seats do not require a human to click `TAKE CONTROL`.
- Dedicated bot seats execute on the server when active.

### Ghost-mode guarantees

- Ghost mode applies only to a disconnected human seat.
- Ghost mode stops immediately when the original player reconnects or ghost mode is disabled/released.

## Settings Icon Visibility Contract

The in-game settings/management action is visible only for disconnected human seats when the current user is the delegated controller and may manage ghost-mode AI for that seat.

```json
{
  "playerIsDisconnected": true,
  "seatType": "HumanSeat",
  "delegatedControllerUserId": "current-user-id",
  "currentUserCanRelease": true
}
```

Dedicated bot seats do not require `TAKE CONTROL` to run their turn.

## AI Assignment Contract

### Dedicated bot seat activation

```json
{
  "gameId": "game-42",
  "playerUserId": "bot-seat-user",
  "controllerMode": "AiBotSeat",
  "botDefinitionId": "bot-iron-strategist"
}
```

### Ghost activation

```json
{
  "gameId": "game-42",
  "playerUserId": "user-disconnected",
  "controllerMode": "AiGhost",
  "controllerUserId": "user-controller",
  "botDefinitionId": "bot-iron-strategist"
}
```

## Phase Resolution Contract

### Eligible phases

- `PickRegion`
- `Purchase`
- `Auction`
- `Sell` using deterministic built-in logic
- `Move` using existing suggested-path logic

### Resolution guarantees

- AI turns are generated only on the server.
- The server may commit only a legal action for the current authoritative state.
- If only one legal choice exists, the server commits it without OpenAI.
- If OpenAI returns an invalid, stale, or missing choice, the server commits the documented fallback for the phase.
- The resulting action is recorded in normal game history and broadcast through the existing multiplayer update path.

## History And Reload Contract

- Persisted player-action event data remains the canonical source for AI attribution when rebuilding timeline/history views.
- Reloaded history items preserve the server actor identity plus bot attribution metadata when the stored event payload includes it.
- Gameplay history surfaces AI attribution separately from the human-readable summary so older summary strings are not the only source of truth.

```json
{
  "actingUserId": "ai://boxcars-server",
  "isAiAction": true,
  "botDefinitionId": "bot-iron-strategist",
  "botName": "Iron Strategist",
  "botDecisionSource": "SuggestedRoute | OpenAI | OnlyLegalChoice | Fallback",
  "botFallbackReason": "optional explanatory text"
}
```

## Authorization Contract

- Human-authored actions continue to use normal direct/delegated slot authorization.
- AI-authored actions are valid only when the active seat controller mode is `AiBotSeat` or `AiGhost`.
- AI-authored actions use a server-owned actor identity rather than a delegated human user identity.

## Stop Conditions Contract

An active AI assignment is cleared or disabled immediately when any of the following occurs:

- Ghost mode is released.
- The disconnected human player reconnects.
- The assignment is replaced with a different bot.
- The referenced bot definition no longer exists.

Dedicated bot seats remain AI-controlled unless explicitly reconfigured.