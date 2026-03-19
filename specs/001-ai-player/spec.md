# Feature Specification: AI-Controlled Player Turns

**Feature Branch**: `001-ai-player`  
**Created**: 2026-03-16  
**Status**: Draft  
**Input**: User description: "AI-Player (aka Bot) I want to make an AI Player which uses GPT-4.0-mini model to make moves on behalf of the player. Each bot has: Name - name of the bot player; Strategy - a text block prompt that describes the strategy the bot should use to make it's decisions. AI Decision - The game state is serialized into prompt that is combined with bot strategy to resolve to make a AI decision. For example: Purchase - The bot has access to the state of the game, the amount of money it has, etc, and so selects a railroad to purchase (or not) as the result. The game records the move the bot player and game play continues. Different phases of the game use AI Decisions versus built in actions. MOVE - it just uses suggested path, no AI Move. PICK REGION - AI Decision according to it's Strategy. PURCHASE - AI Decision according to it's Strategy. AUCTION - AI Decision according to it's strategy. SELL - Sells the RR that has mimimal impact on the Access/Monopoly. The AI will use a GPT-4o-mini API model and access the API key using the web settings property OpenAIKey. Actions that are decided by AI Decision are recorded just like any actions made by any player."

## Clarifications

### Session 2026-03-16

- Q: How is bot control assigned for a disconnected player during live multiplayer play? → A: When a player is disconnected and another player has taken control of that seat, the player card must show a settings icon in addition to the RELEASE button. The controlling player can use that settings action to assign a bot to act on behalf of the disconnected player. The bot continues acting for that player until the original player reconnects or the controlling player clicks RELEASE.
- Q: How are bots configured for assignment during gameplay? → A: Bots are defined outside gameplay on the dashboard page through a BOT management experience that lets users create and manage reusable bot strategy definitions. During gameplay, the controlling player assigns one of those predefined bots to the disconnected player.
- Q: Do current bot assignments keep a snapshot of the bot definition or follow later dashboard edits? → A: Current assignments remain live references to the dashboard bot definition, so editing a bot on the dashboard immediately changes any active assignments using that bot.
- Q: What scope should the dashboard bot library use? → A: Bot definitions are global. One shared bot library is available to all users and all games.
- Q: Who can manage the global bot library? → A: Any signed-in user can create, edit, and delete global bots.
- Q: Should dedicated bot players require another player to click TAKE CONTROL before they can act? → A: No. Dedicated bot seats are already AI-controlled and must execute their turns automatically on the server without delegated human control.
- Q: How should disconnected human seats differ from dedicated bot seats? → A: Disconnected human seats may enter ghost mode and use AI only while disconnected and explicitly ghost-enabled; dedicated bot seats are permanently AI-controlled seats with different UX and stop conditions.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Let a bot take its turn (Priority: P1)

As a player in a multiplayer game, I want AI-controlled seats to resolve their eligible turns without requiring a human to manually drive them so the game can continue when a seat is a dedicated bot or when a disconnected human seat is ghosted.

**Why this priority**: The core value of the feature is that AI-controlled seats do not block turn progression. If a dedicated bot seat or ghosted human seat cannot take legal actions automatically, the feature does not deliver playable value.

**Independent Test**: Can be fully tested by running one game with a dedicated bot seat and another with a disconnected ghosted human seat, then verifying that the server resolves each eligible phase without further human input while preserving legal game flow.

**Acceptance Scenarios**:

1. **Given** a game includes a dedicated bot seat, **When** that seat becomes active, **Then** the server resolves eligible AI-backed phases for that seat without any human needing to click TAKE CONTROL.
2. **Given** a human player is disconnected and another player has taken control of that seat, **When** the controlling player opens the controlled player's card, **Then** the card shows a settings icon alongside the RELEASE button so ghost-mode AI can be configured.
3. **Given** an AI-controlled seat reaches destination-region selection, **When** the phase resolves, **Then** the system chooses one legal destination region for that seat and advances the game as if the seat had chosen it directly.
4. **Given** an AI-controlled seat reaches a purchase opportunity with one or more legal purchase options, **When** the purchase phase resolves, **Then** the system applies one legal purchase decision or a legal decision to buy nothing and continues gameplay.
5. **Given** an AI-controlled seat reaches an auction with legal bid or pass choices, **When** the auction turn resolves, **Then** the system applies one legal auction action for that seat and continues the auction flow.
6. **Given** an AI-controlled seat is in the movement phase, **When** movement is resolved, **Then** the system uses the existing suggested route behavior rather than requesting a separate AI decision.
7. **Given** an AI-controlled seat must sell railroads to raise funds, **When** the sell phase resolves, **Then** the system sells the railroad whose loss has the least negative effect on the seat's access and monopoly position and continues gameplay.
8. **Given** a ghosted disconnected human seat is AI-controlled, **When** the original player reconnects or ghost mode is disabled, **Then** the AI stops acting on behalf of that seat.

---

### User Story 2 - Define bot identity and strategy (Priority: P2)

As a user, I want a dashboard BOT management experience where I can define reusable bot strategies in a shared global library so controlling players can assign those bots quickly during live games.

**Why this priority**: A shared global bot library removes ad hoc in-game authoring, keeps strategy management outside the turn flow, and lets the same curated bots be assigned consistently across all games.

**Independent Test**: Can be fully tested by creating and editing bot definitions from the dashboard, joining different live games, assigning those predefined bots to dedicated bot seats and ghosted disconnected human seats, and confirming the assigned bot's current global definition is used until the assignment ends.

**Acceptance Scenarios**:

1. **Given** a user opens the dashboard BOT management experience, **When** they create a bot definition, **Then** the system stores that bot definition with a reusable name and strategy in the global bot library.
2. **Given** an existing bot definition in the global bot library, **When** a signed-in user updates it from the dashboard BOT management experience, **Then** the updated definition is available for later assignment during gameplay in any game.
3. **Given** an existing bot definition in the global bot library, **When** a signed-in user deletes it from the dashboard BOT management experience, **Then** that definition is no longer available for new assignment during gameplay.
4. **Given** a dedicated bot seat or a ghosted disconnected human seat references a predefined bot, **When** that AI-controlled seat takes a turn, **Then** the system uses that bot definition and keeps the assignment linked to later updates of that same bot definition.
5. **Given** multiple AI-controlled seats exist in the same or different games, **When** each assigned bot takes a turn, **Then** each bot uses the global bot definition selected for that seat rather than another bot's configuration.

---

### User Story 3 - Preserve bot actions as normal game history (Priority: P3)

As a player reviewing the game, I want AI-driven decisions taken for an AI-controlled seat recorded the same way as human actions so turn history, reloading, and multiplayer synchronization remain trustworthy.

**Why this priority**: Boxcars is event-driven and multiplayer-first. Bot actions must fit the same audit and synchronization model as any other player action or the game state becomes harder to trust and restore.

**Independent Test**: Can be fully tested by letting both a dedicated bot seat and a ghosted disconnected human seat complete several eligible phases, reloading the game state, and verifying that the recorded action history and resulting board state match the earlier AI choices.

**Acceptance Scenarios**:

1. **Given** an AI-controlled seat completes an automated decision phase, **When** the action is committed, **Then** the resulting action is recorded in game history as an action taken by that seat's player.
2. **Given** connected players are observing a game with AI-controlled seats, **When** an AI action is recorded, **Then** all players receive the same resulting game-state update they would receive for a human player's action.
3. **Given** a saved game contains previously recorded AI actions, **When** the game is reloaded, **Then** the restored game state reflects those AI actions exactly as recorded.

### Edge Cases

- If an AI-controlled seat reaches an AI-driven phase with only one legal action, the system must complete that action without requesting an unnecessary choice.
- If an AI decision cannot be interpreted as a legal action, the system must ignore the invalid result, choose a legal fallback for that phase, and keep the game moving.
- If no external decision result is available in time for a purchase or auction choice, the system must fall back to a legal no-purchase, no-bid, or pass-style outcome rather than leaving the game stalled.
- If no external decision result is available in time for destination-region selection, the system must choose one legal region so the turn can continue.
- If multiple railroad sale options have the same minimal effect on access and monopoly position, the system must choose consistently so repeated evaluations of the same state produce the same sale.
- If a bot's stored strategy is blank, the system must still allow the AI to act using only the current game state and the default decision behavior for that phase.
- If an AI action becomes illegal before commitment because the game state changed, the system must re-evaluate against the current legal choices before recording the action.
- If a dedicated bot seat becomes active, the system must not require any human user to take control before that seat can act.
- If a disconnected human seat is not in ghost mode, the system must not let AI act on behalf of that seat.
- If ghost mode is active and the original player reconnects, the AI must stop acting on behalf of that seat immediately.
- If a player is disconnected but no other player has taken control of that human seat, the system must not show the ghost-assignment settings action.
- If no bot definitions exist when a controlling player opens the assignment settings action, the system must make it clear that no bot can be assigned yet.
- If a bot definition is deleted or becomes unavailable before assignment, the system must not allow that missing definition to be assigned.
- If a dashboard bot definition is edited while it is actively assigned, the next AI decision using that assignment must use the updated definition.
- If a global bot definition is removed while it is actively assigned, the system must fail that assignment safely and prevent further AI decisions until a valid bot is assigned or the assignment is cleared.
- If multiple signed-in users edit the same global bot definition close together, the system must avoid silently applying conflicting changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support AI-controlled seats through two modes: dedicated bot seats and ghost-controlled disconnected human seats.
- **FR-002**: Dedicated bot seats MUST execute eligible automated turns without requiring any human player to click TAKE CONTROL.
- **FR-003**: When a human player is disconnected and another player has taken control of that seat, the controlled player's card MUST show a settings icon in addition to the RELEASE button so ghost-mode AI can be configured.
- **FR-003A**: Activating the settings action from that controlled player's card MUST allow the controlling player to assign one predefined bot to that disconnected human seat.
- **FR-004**: The dashboard page MUST provide a BOT management experience for creating and managing reusable bot strategy definitions outside gameplay.
- **FR-005**: Each bot strategy definition MUST include a bot name and strategy text.
- **FR-006**: The system MUST maintain bot strategy definitions in one shared global library available across all users and all games.
- **FR-006A**: Any signed-in user MUST be able to create, edit, and delete bot strategy definitions in the global bot library.
- **FR-007**: Each AI seat assignment MUST keep a live reference to the selected bot strategy definition rather than copying a separate strategy snapshot into the assignment.
- **FR-008**: Editing a bot strategy definition on the dashboard MUST immediately affect any active assignments that reference that bot definition.
- **FR-008A**: Deleting a bot strategy definition from the dashboard MUST remove it from future assignment choices.
- **FR-009**: When an AI-controlled seat reaches a game phase that requires an automated choice, the system MUST resolve that phase without requiring additional human input for that seat.
- **FR-010**: The system MUST use an AI-driven decision for destination-region selection by an AI-controlled seat.
- **FR-011**: The system MUST use an AI-driven decision for purchase choices by an AI-controlled seat.
- **FR-012**: The system MUST use an AI-driven decision for auction choices by an AI-controlled seat.
- **FR-013**: The system MUST use the existing suggested-path behavior for movement by an AI-controlled seat instead of requesting a separate AI decision.
- **FR-014**: The system MUST use a built-in sell decision for an AI-controlled seat that chooses the railroad sale with the least negative effect on that seat's access and city-monopoly position.
- **FR-015**: Before requesting an AI-driven choice, the system MUST construct the decision from the current authoritative game state and the currently referenced bot strategy definition.
- **FR-016**: AI-driven bot choices MUST be limited to actions that are legal for the current phase and current game state.
- **FR-017**: If an AI-driven result is invalid, unavailable, or no longer legal at commitment time, the system MUST resolve the phase using a legal fallback that keeps gameplay moving.
- **FR-018**: The legal fallback for destination-region selection MUST choose one valid region.
- **FR-019**: The legal fallback for purchase resolution MUST choose a valid no-purchase outcome when no valid AI-selected purchase can be committed.
- **FR-020**: The legal fallback for auction resolution MUST choose a valid pass or no-bid outcome when no valid AI-selected bid can be committed.
- **FR-021**: The system MUST apply the same server-side validation to bot actions that it applies to human player actions.
- **FR-021A**: The system MUST authorize server-authored AI actions only when the active seat is currently in an AI controller mode.
- **FR-022**: After an AI action is validated, the system MUST record it in game history as an action taken by the player represented by that seat.
- **FR-023**: AI actions MUST update connected players through the same real-time game-state flow used for human actions.
- **FR-024**: Reloading a saved game MUST restore the effects of previously recorded AI actions exactly as recorded.
- **FR-025**: If multiple AI-controlled seats exist in one game, the system MUST keep each bot reference and resulting decisions isolated to the specific seat it is representing.
- **FR-026**: If only one legal outcome exists for an AI-driven phase, the system MUST apply that outcome directly without requiring an external decision response.
- **FR-027**: The system MUST resolve repeated sell evaluations for the same game state consistently when multiple legal railroad sales have identical minimal impact.
- **FR-028**: When ghost mode is disabled or the controlling player clicks RELEASE for a disconnected human seat with an assigned bot, the AI MUST stop acting on behalf of that seat.
- **FR-029**: When the disconnected human player reconnects, the ghost assignment MUST stop acting on behalf of that seat.
- **FR-030**: The system MUST NOT expose the ghost-assignment settings action for connected human seats, dedicated bot seats, or disconnected human seats whose seat is not currently under take-control by another player.
- **FR-031**: If no bot strategy definitions currently exist in the global bot library, the assignment experience MUST indicate that no bot can be assigned until a bot is defined on the dashboard.
- **FR-032**: The system MUST prevent silent loss of changes when multiple signed-in users attempt to modify the same global bot strategy definition concurrently.

### Key Entities *(include if feature involves data)*

- **Seat Controller Mode**: The current control ownership for a seat, distinguishing direct human control, delegated human control, dedicated bot AI, and ghost-mode AI.
- **Bot Assignment**: A durable per-seat AI association, including the represented seat, bot identity, controller mode, and whether the assignment is still active.
- **Bot Strategy Definition**: A reusable dashboard-managed bot definition, including the bot's display name and stored strategy text.
- **Global Bot Library**: The shared set of bot strategy definitions available to all users and all games.
- **Bot Library Edit**: A create, update, or delete action performed by a signed-in user against a bot strategy definition in the global bot library.
- **Assigned Bot Reference**: The live link from an AI-controlled seat's assignment to the selected dashboard bot strategy definition currently acting for that seat.
- **Bot Decision Context**: The authoritative game-state snapshot and phase-specific legal choices used to determine an AI action for the represented seat.
- **Bot Decision Outcome**: The resolved action chosen on behalf of an AI-controlled seat during a specific game phase, including whether it came from an AI-driven choice or a built-in fallback.
- **Sell Impact Evaluation**: The comparison data used to judge which railroad sale has the least negative effect on the AI-controlled seat's access and city-monopoly position.
- **Recorded Bot Action**: A committed game-history entry showing the action taken on behalf of an AI-controlled seat and the resulting state change.

## Assumptions & Dependencies

- Dedicated bot seats are permanently AI-controlled seats and do not require delegated human control.
- Ghost mode is an AI controller mode for a disconnected human seat and is distinct from a dedicated bot seat.
- Bot strategy definitions are managed outside gameplay from the dashboard in a single shared global library and can be reused across assignments in any game.
- Active assignments remain linked to their dashboard bot strategy definitions, so later definition edits immediately affect future bot decisions for those assignments.
- Any signed-in user is allowed to manage the global bot library.
- The current game-state serialization already contains the information needed to describe legal choices for destination-region selection, purchase, auction, and sell phases.
- The existing movement suggestion behavior is already trustworthy enough to serve as the bot movement mechanism for this feature.
- Choosing not to buy or not to bid is a legal outcome when the current rules allow the active player to decline those actions.
- Access and city-monopoly position are the authoritative measures for determining the least harmful railroad sale.
- A blank strategy text means the bot should rely on the game-state context and default decision behavior rather than blocking play.
- Releasing ghost mode or reconnecting the disconnected human player ends the ghost assignment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In end-to-end gameplay tests, 100% of turns for dedicated bot seats and ghost-controlled human seats complete every eligible decision phase without requiring manual input for that seat.
- **SC-002**: In validation tests, 100% of committed AI actions are legal for the phase and game state in which they were recorded.
- **SC-003**: In reload tests, 100% of previously recorded AI actions reproduce the same restored game state as before the save.
- **SC-004**: In multiplayer observation tests, 100% of AI actions appear to connected players through the same update flow and history model as human actions.
- **SC-005**: In sell-resolution tests, 100% of forced railroad sales by an AI-controlled seat choose a sale whose impact on access and city-monopoly position is no worse than any other legal sale option.
- **SC-006**: In configuration tests, each assigned bot consistently uses its own stored name and strategy for 100% of its AI-driven decisions while assigned to its seat.

