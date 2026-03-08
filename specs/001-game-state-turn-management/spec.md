# Feature Specification: Game State and Turn Management Cleanup

**Feature Branch**: `[001-game-state-turn-management]`  
**Created**: 2026-03-07  
**Status**: Draft  
**Input**: User description: "clean up all of the UI around game state and turn management.
* When game is loaded/reloaded the last event record in table storage should be loaded to get the current game state, and the board should reflect that.
* The gameboard should always reflect the partial information to help user make their move. This means The current Moves Left and Cost should be shown, and as the player selects the segments, the Moves left and cost should be updated. The user should not be able to hit END TURN until they have selected all of their moves, and they should not be able to add segments if there are no moves left.
* The route segments selected should be stored in the event record, and all of the segments traveled on for the Start->Destination need to be kept so that the X on previous traveled can be preserved.
* If the user gets to the destination city, then they should be alerted that they have arrived, the payout should be added to their cash, and the opportunity to buy should be offered (TBD).
* When a player hits END TURN, then the next player should be the active player.
* Players can only make choices for their player"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Restore the live board from saved play (Priority: P1)

As a returning player, I can reload a saved game and immediately see the active player, board markings, and current turn state exactly as they were last recorded.

**Why this priority**: If reloaded state is wrong, every later turn-management action becomes unreliable and multiplayer play can desynchronize.

**Independent Test**: Can be fully tested by saving a game mid-turn with partial movement already selected, reloading the game, and verifying the board, active player, remaining movement, accumulated route cost, and prior travel markings all match the latest recorded event.

**Acceptance Scenarios**:

1. **Given** a game has persisted event records, **When** the game is loaded or reloaded, **Then** the system restores game state from the most recent event record and displays the same active player, movement progress, and board state that existed when that event was recorded.
2. **Given** a player has already traveled part of a start-to-destination path, **When** the game is reloaded, **Then** the board preserves the traveled-path markers for every segment already taken on that trip.
3. **Given** the latest recorded event contains selected route segments for an unfinished turn, **When** the game is reloaded, **Then** those selected segments reappear and the turn remains in the same incomplete state.

---

### User Story 2 - Plan movement with live turn feedback (Priority: P1)

As the active player, I can see movement allowance and travel cost update as I choose segments so I know whether my turn plan is valid before ending the turn.

**Why this priority**: Clear partial-turn feedback is the main usability problem described in the request and directly affects whether players can make legal moves.

**Independent Test**: Can be tested by starting a turn with a known number of moves, selecting segments one by one, and verifying the board updates moves left and cost after each selection while preventing extra movement and premature turn completion.

**Acceptance Scenarios**:

1. **Given** it is the active player's move phase, **When** the board is displayed, **Then** the current moves left and current route cost are visible before any new segment is selected.
2. **Given** the active player selects a valid next segment, **When** the selection is applied, **Then** the board updates the selected path, decreases moves left appropriately, and updates the route cost immediately.
3. **Given** the active player has not used all available moves, **When** they attempt to end the turn, **Then** the system blocks the action and explains that all movement must be allocated first.
4. **Given** the active player has no moves left, **When** they attempt to add another segment, **Then** the system rejects the additional selection and leaves the current route unchanged.

---

### User Story 3 - Complete arrival and advance the turn (Priority: P2)

As a player who reaches a destination, I can see arrival resolved immediately and the game can advance cleanly to the next player's turn when the turn ends.

**Why this priority**: Arrival payout and active-player progression are core game-loop behaviors that must remain faithful to Rail Baron rules.

**Independent Test**: Can be tested by moving a player onto their destination city, confirming arrival messaging and payout, ending the turn, and verifying the next player becomes active.

**Acceptance Scenarios**:

1. **Given** the active player's selected movement reaches the destination city, **When** the final segment is committed, **Then** the system notifies that player of arrival and adds the destination payout to that player's cash.
2. **Given** the active player has reached the destination city, **When** arrival is processed, **Then** the system presents a buy opportunity for the arrival state so the player can proceed with the next decision point defined by game rules.
3. **Given** the active player has completed all required movement and ends the turn, **When** turn advancement runs, **Then** the next player in turn order becomes the active player.

---

### User Story 4 - Restrict actions to the owning player (Priority: P2)

As a participant in a multiplayer game, I can only make movement and turn decisions for my own player so other players cannot alter my turn.

**Why this priority**: Server-authoritative turn enforcement is required for fair concurrent play and is explicitly mandated by the project constitution.

**Independent Test**: Can be tested with two connected players by attempting movement, segment selection, and turn-ending actions from both the active player's client and another player's client.

**Acceptance Scenarios**:

1. **Given** it is Player A's turn, **When** Player B attempts to select or change route segments for that turn, **Then** the system rejects the action and preserves Player A's current selection.
2. **Given** it is Player A's turn, **When** Player B attempts to end the turn, **Then** the system rejects the action and Player A remains active.
3. **Given** the turn advances to Player B, **When** Player B begins making choices, **Then** Player B can control only their own turn state and not any other player's state.

### Edge Cases

- If a game reloads with no persisted events, the system falls back to the defined initial game state instead of showing stale or partial board data.
- If the latest persisted event is incomplete or invalid, the system fails safely with a recoverable load error rather than presenting a misleading board state.
- If a player's remaining movement is partially consumed when a game reloads, the board shows the remaining allowance exactly as recorded rather than recalculating from a clean turn.
- If a player reaches the destination city before using every possible step from the roll, arrival resolution follows the recorded game rules for destination completion and does not allow illegal extra travel beyond the destination.
- If the same segment is already part of the preserved traveled path for the current trip, reloading does not duplicate or lose its traveled marker.
- If a non-active player reconnects while another player is mid-turn, that reconnecting player can observe the current partial route state but cannot change it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST restore the current game state from the latest persisted game event whenever a game is loaded or reloaded.
- **FR-002**: System MUST display board state after reload in a way that matches the restored event state, including active player, selected route segments, prior traveled-path markers for the current trip, moves left, and current route cost.
- **FR-003**: System MUST persist the route segments selected during a player's turn as part of the recorded game event for that turn.
- **FR-004**: System MUST persist the full set of segments already traveled on the player's current start-to-destination trip so prior-travel markings can be restored accurately.
- **FR-005**: System MUST show the active player's remaining movement allowance and accumulated route cost throughout turn planning.
- **FR-006**: System MUST recalculate and display remaining movement allowance immediately after each segment selection or deselection that changes the planned path.
- **FR-007**: System MUST recalculate and display accumulated route cost immediately after each segment selection or deselection that changes the planned path.
- **FR-008**: System MUST prevent the active player from selecting additional route segments once no movement allowance remains.
- **FR-009**: System MUST prevent ending the turn until the active player has completed all required movement selection for the current turn.
- **FR-010**: System MUST preserve any already-selected legal route segments when an invalid extra segment is attempted.
- **FR-011**: System MUST notify the active player immediately when their selected movement reaches the destination city.
- **FR-012**: System MUST add the destination payout to the arriving player's cash when destination arrival is resolved.
- **FR-013**: System MUST present the next arrival decision point, including the opportunity to buy where applicable, after destination arrival is resolved.
- **FR-014**: System MUST advance the active turn to the next player in turn order after a valid end-turn action completes.
- **FR-015**: System MUST update all connected players' views so they can see the new active player and the completed board state after turn advancement.
- **FR-016**: System MUST allow only the active player's controlling participant to select route segments, modify that turn's pending movement, or end that turn.
- **FR-017**: System MUST reject movement-selection and turn-ending actions from any participant who is not controlling the active player.
- **FR-018**: System MUST preserve turn state correctly across reloads even when the saved game is mid-turn rather than between turns.
- **FR-019**: System MUST keep arrival payout, prior-travel markings, and active-player progression consistent with official Rail Baron rules.

### Key Entities *(include if feature involves data)*

- **Game Event Record**: The latest recorded gameplay event used to reconstruct the current turn state, including active player, selected segments, traveled segments for the current trip, and turn progress.
- **Turn Movement State**: The active player's in-progress movement data, including movement allowance granted, movement remaining, accumulated route cost, and currently selected route segments.
- **Trip Travel History**: The ordered set of segments already traveled from the player's trip origin toward the current destination, used to preserve board markings across events and reloads.
- **Arrival Resolution**: The state created when a player reaches the destination city, including arrival notification, payout amount, and next decision opportunity.
- **Player Control Context**: The relationship between a connected participant and the player identity they are allowed to control for turn decisions.

## Assumptions & Dependencies

- The game already records gameplay as an ordered sequence of persistent events, and the latest event is the authoritative source for a reload.
- The current product already has a destination payout table and turn order model that this feature will reuse rather than redefine.
- "Opportunity to buy" means the UI must surface the arrival purchase decision point; any deeper purchase workflow not already defined elsewhere remains governed by existing game rules and adjacent features.
- The board already has a visual treatment for prior traveled segments, and this feature focuses on preserving and restoring that information accurately.
- All turn validation remains server-authoritative even if the client shows disabled or hidden controls.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In reload tests covering mid-turn and end-of-turn saves, 100% of reloaded games show the same active player, selected route, moves left, route cost, and current-trip traveled markers as the latest recorded event.
- **SC-002**: In turn-planning tests, 100% of legal segment selections update moves left and route cost within the same interaction cycle.
- **SC-003**: In turn-planning tests, 100% of attempts to end a turn early or add segments after movement is exhausted are rejected without altering the previously valid route.
- **SC-004**: In arrival tests, 100% of destination completions notify the player and apply the correct payout before the next turn begins.
- **SC-005**: In multiplayer authorization tests, 100% of non-owning-player attempts to change another player's turn state are rejected.
