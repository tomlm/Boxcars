# Feature Specification: AI Advice Chat

**Feature Branch**: `[001-ai-advice-chat]`  
**Created**: 2026-03-20  
**Status**: Draft  
**Input**: User description: "add AI advice chat window

I want a chat icon in the lower right that opens up a sidebar chat experience with AI.
When the player clicks on it, it should show a chat window with OpenAI. The AI should have access to the game state as context, and open the conversation with \"How can I help?\"
The AI should answer any questions with the player with the current state of the gameboard and the strategy of the player."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open The Advisor From The Board (Priority: P1)

As a player on the game board, I want a visible chat entry point that opens an AI advice sidebar without leaving the board so I can ask for help during my turn.

**Why this priority**: The feature is not usable unless players can reliably discover and open the advisor while staying in the live game flow.

**Independent Test**: Can be fully tested by opening a live game board, clicking the advisor icon, and verifying that a sidebar chat experience opens with the expected greeting.

**Acceptance Scenarios**:

1. **Given** a player is viewing an active game board, **When** the player clicks the advisor icon in the lower-right corner, **Then** a sidebar chat panel opens on the same page.
2. **Given** the sidebar chat panel opens for the first time in the current page session, **When** the panel becomes visible, **Then** the assistant begins the conversation with "How can I help?"
3. **Given** the sidebar chat panel is already open, **When** the player closes it, **Then** the game board remains visible and playable.

---

### User Story 2 - Ask Board-Aware Questions (Priority: P2)

As a player, I want to ask natural-language questions and receive answers grounded in the current game board so the advice reflects my actual situation instead of generic Rail Baron guidance.

**Why this priority**: The main value of the advisor is relevance. Answers that do not reflect the live board state would be misleading and reduce trust.

**Independent Test**: Can be fully tested by asking questions about cash, destination, rail holdings, fees, turn phase, and route pressure, then verifying that the responses align with the latest authoritative game state.

**Acceptance Scenarios**:

1. **Given** the advisor sidebar is open, **When** the player submits a question about the current game situation, **Then** the assistant responds using the latest available state from that game.
2. **Given** the game state changes after a move, purchase, sale, payout, or fee resolution, **When** the player asks a follow-up question, **Then** the assistant uses the updated state rather than stale board information.
3. **Given** the player asks a question about a board element that is not currently applicable, **When** the assistant answers, **Then** it clearly states the relevant current condition instead of inventing nonexistent state.

---

### User Story 3 - Receive Strategy-Tailored Guidance (Priority: P3)

As a player, I want the assistant to tailor its answers to my position and likely strategy so the advice feels personal to my game rather than general commentary.

**Why this priority**: Personalized advice increases usefulness, especially when the same board state can imply different choices depending on cash pressure, engine type, owned railroads, destination odds, and current objective.

**Independent Test**: Can be fully tested by comparing advisor responses for different players or different board states and confirming that the recommendations shift with the player context.

**Acceptance Scenarios**:

1. **Given** the advisor has access to the current player's state, **When** the player asks for strategic help, **Then** the answer references the player's current position, resources, and immediate decision context.
2. **Given** the player controls a different seat or delegated seat than before, **When** the player opens the advisor or sends the next message, **Then** the assistant uses the newly controlled player's context.

---

### Edge Cases

- What happens when the player opens the advisor while no controllable player seat is currently associated with the session?
- What happens when the game state changes while the sidebar is open but before the player sends the next message?
- How does the system respond when the AI service is temporarily unavailable or times out?
- How does the system prevent advice from being presented as authoritative rule resolution when the server outcome is final?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The game board MUST display a persistent advisor entry point in the lower-right area of the page whenever a user is viewing a game board.
- **FR-002**: When the user activates the advisor entry point, the system MUST open a sidebar chat experience without navigating away from the game board.
- **FR-003**: When the sidebar chat is opened for the current page session, the assistant MUST begin with the message "How can I help?"
- **FR-004**: The sidebar MUST allow the user to submit freeform questions and view the full conversation thread for the current page session.
- **FR-005**: For each user question, the assistant MUST receive the latest available authoritative game context for the current game before generating a response.
- **FR-006**: The context supplied to the assistant MUST include the current game board situation relevant to advice, including at minimum turn phase, active player, current controlled player, cash position, engine, destination status, owned railroads, fee pressure, and other immediately relevant board conditions.
- **FR-007**: The assistant MUST tailor responses to the player context associated with the current user session or the seat the user is currently controlling.
- **FR-008**: If the user asks a question after the game state changes, the assistant MUST answer from refreshed game context rather than prior cached advice context alone.
- **FR-009**: The assistant MUST present its output as advisory help and MUST NOT imply that it can override authoritative game rules or server outcomes.
- **FR-010**: If advice cannot be generated, the sidebar MUST show a clear failure message and allow the user to try again without losing the rest of the board.
- **FR-011**: The chat experience MUST preserve the visible conversation history while the sidebar remains available during the current page session.
- **FR-012**: The chat experience MUST remain usable on both desktop and mobile board layouts.

### Assumptions

- The advisor is available to authenticated users viewing a game board; strategy context is based on the seat the user currently controls, or the seat most relevant to the user session if only one applies.
- Advice is intended to help interpret the current game situation, not to take actions automatically.
- Existing game state already contains enough information to describe the player's tactical position without adding new gameplay rules.

### Key Entities *(include if feature involves data)*

- **Advisor Conversation Session**: The visible sidebar conversation tied to the current board page session, including the greeting, user questions, assistant replies, and session-level availability state.
- **Advisor Context Snapshot**: The assembled advisory view of the current game state used for a reply, including game identity, player identity, board state, current decision phase, and player strategy-relevant facts.
- **Advisor Entry Point State**: The board-level state that determines whether the advisor is collapsed or open and whether it is ready, loading, or unavailable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A player can open the advisor from the live game board and see the greeting message in 5 seconds or less under normal in-game conditions.
- **SC-002**: In acceptance testing, 90% or more of advisor responses to board-state questions accurately reflect the current authoritative game state at the time the question is sent.
- **SC-003**: In acceptance testing across at least three distinct player situations, the advisor produces materially different strategy guidance when the controlled player's cash, engine, destination pressure, or railroad ownership changes.
- **SC-004**: When the advice service is unavailable, 100% of failed requests surface a clear retryable message without forcing the player to refresh or leave the board.
