# Quickstart: Game State and Turn Management Cleanup

**Feature**: 001-game-state-turn-management  
**Date**: 2026-03-07

## Prerequisites

- .NET 8 SDK
- Valid Azure Table Storage connection string in app settings or user secrets
- Existing `Boxcars.slnx` solution
- Feature branch `001-game-state-turn-management`

## Relevant Project Areas

```text
src/Boxcars/Components/Pages/GameBoard.razor
src/Boxcars/Components/Map/
src/Boxcars/GameEngine/
src/Boxcars/Data/
src/Boxcars.Engine/Domain/
src/Boxcars.Engine/Persistence/GameState.cs
tests/Boxcars.Engine.Tests/
tests/Boxcars.GameEngine.Tests/
```

## Build and Test

```powershell
dotnet build Boxcars.slnx
dotnet test Boxcars.slnx
```

## Manual Validation Flow

### 1. Verify reload restores the latest event snapshot

1. Create a game with at least two players.
2. Start a turn, draw destination, roll, and select part of a route without finishing the turn.
3. Refresh the browser or reconnect to the board.
4. Confirm the board restores:
   - active player
   - current phase
   - moves left
   - selected route preview
   - traveled X markers for the current trip

### 2. Verify live move/cost planning feedback

1. As the active player, enter move mode.
2. Select route segments one by one.
3. Confirm moves left and fee estimate update immediately after each legal change.
4. Confirm additional segment selection is blocked once no movement remains.
5. Confirm `END TURN` stays unavailable until movement requirements are satisfied.

### 3. Verify arrival handling

1. Move onto the destination city.
2. Confirm an arrival message is shown.
3. Confirm the player's cash increases by the expected payout.
4. Confirm the board surfaces the next arrival decision point, including purchase opportunity when applicable.

### 4. Verify active-player-only control

1. Open the same game as two different users.
2. On the inactive player's session, attempt route selection and turn-ending actions.
3. Confirm the inactive player sees current board state but cannot mutate the turn.
4. Confirm the active player's valid action advances the turn and the next player becomes active on both sessions.

## Implementation Notes

- Keep reload logic centered on the latest `GameEventEntity.SerializedGameState`.
- Use MudBlazor components for any new turn-status, action, or notification UI.
- Preserve server-side action validation even if the UI prevents invalid interactions earlier.