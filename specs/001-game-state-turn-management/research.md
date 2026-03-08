# Research: Game State and Turn Management Cleanup

**Feature**: 001-game-state-turn-management  
**Date**: 2026-03-07

## R1: Reload state from the latest persisted event snapshot

**Decision**: Keep `GameEventEntity.SerializedGameState` on the highest-sort event row as the single authoritative restore source for game reloads and reconnects.

**Rationale**: The current `GameEngineService.GetOrCreateGameEngineAsync` path already restores the latest event snapshot into `Boxcars.Engine.Domain.GameEngine`. Reusing that path avoids introducing a second reconstruction mechanism and preserves the constitution requirement that server state remain authoritative. The feature gap is not storage availability; it is getting the UI to render all relevant restored state, especially partial-turn selections and traveled markers.

**Alternatives considered**:
- Recompute reload state by replaying every historical event: rejected because the latest snapshot already exists, and full replay adds complexity without solving the UI gap.
- Restore from `GameEntity` only and reconstruct turn state heuristically: rejected because it loses mid-turn fidelity and violates the spec's requirement to reflect the latest event record exactly.

## R2: Represent partial-turn selection separately from committed travel history

**Decision**: Treat in-progress selected route segments and completed traveled segments as distinct concepts in persisted turn state and UI mapping.

**Rationale**: `GameState.PlayerState.ActiveRoute` and `UsedSegments` already capture related but different ideas. The feature requires both: the current selected path for an unfinished turn, and the full traveled path for the current start-to-destination trip so prior X markers persist. Keeping them separate prevents reload logic from confusing a tentative route preview with already-completed movement.

**Alternatives considered**:
- Use only `UsedSegments` for both preview and history: rejected because it collapses tentative selection into committed movement and makes undo/refresh behavior ambiguous.
- Use only `ActiveRoute`: rejected because `ActiveRoute` is a plan, not proof of which segments have actually been traveled and marked on the board.

## R3: Use stable player identity for action authorization, not display name alone

**Decision**: Bind action authorization to the authenticated participant's assigned player slot or user id from `GameEntity.PlayersJson`, and treat display names as presentation only.

**Rationale**: The current `GameEngineService.ProcessTurn` check compares `action.PlayerId` to `CurrentTurn.ActivePlayer.Name`. That blocks many invalid actions, but it still trusts a display-oriented identifier that can drift from the authenticated user mapping. The feature explicitly requires that players can only make choices for their own player, so the contract should use a stable identity resolved from the game roster.

**Alternatives considered**:
- Continue using player display names as the server authorization key: rejected because it is weaker than the user-to-player assignment already stored in `PlayersJson`.
- Move all enforcement to the client by hiding controls: rejected because the constitution requires server-side turn validation.

## R4: Preserve immediate local move/cost feedback while validating committed actions on the server

**Decision**: Keep segment-selection previews and fee/move calculations immediate in the map component, but gate `MoveAction` and `EndTurnAction` against server-side validation using the restored snapshot state.

**Rationale**: The map already knows how to count selected segments and estimate route fees. That gives the active player fast feedback without a round trip for every click. The authoritative checks still belong in the engine/service layer so a stale or manipulated client cannot end early, exceed movement, or move another player's piece.

**Alternatives considered**:
- Send every segment click to the server for validation before updating the board: rejected as more complex and slower for normal map interaction.
- Trust the local preview and commit move/end turn without additional server checks: rejected because it violates the real-time multiplayer principle.

## R5: Decompose the GameBoard page into focused MudBlazor sections

**Decision**: Split `GameBoard.razor` into focused UI sections for turn status/actions, event timeline, and player summary while keeping the map component responsible for board interaction and rendering.

**Rationale**: `GameBoard.razor` currently combines state loading, hub wiring, player mapping, turn automation, and all UI controls in one page. The constitution explicitly requires component decomposition for major UI sections, and this feature is mostly a UI cleanup around turn management. Breaking the page into focused sections makes the turn-status rules easier to reason about and reduces the risk of further entangling placeholder action logic.

**Alternatives considered**:
- Leave the page monolithic and only add more computed properties: rejected because it deepens the existing maintenance problem.
- Move all state orchestration into the map component: rejected because the map should render interactions, not own page-level turn workflow, notifications, and event timeline concerns.

## R6: Surface arrival as an explicit UI state after engine payout resolution

**Decision**: Treat arrival as a server-resolved engine state transition that drives a dedicated UI notification and next-action prompt on the board.

**Rationale**: The domain engine already performs payout and phase progression on arrival. The missing piece is a board-level user cue that the destination was reached and the player can proceed to the next decision point, including purchase opportunity where applicable. This keeps rule execution in the engine while making the UI state explicit and testable.

**Alternatives considered**:
- Keep arrival visible only through the event timeline: rejected because it is too passive for a primary turn transition.
- Add arrival-only client logic before the engine resolves payout: rejected because it would duplicate server-side rule handling.