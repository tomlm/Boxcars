# Research: AI Advice Chat

## Decision 1: Keep OpenAI calls server-side and reuse the existing Boxcars configuration

- **Decision**: Reuse the existing `BotOptions` configuration and server-side `HttpClient` OpenAI integration for advisory replies.
- **Rationale**: The project already has validated OpenAI configuration, timeout handling, and error logging on the server. Reusing that path keeps secrets off clients and aligns with server-authoritative multiplayer architecture.
- **Alternatives considered**:
  - Client-side OpenAI calls: rejected because credentials would be exposed and authority boundaries would blur.
  - Introduce a separate SDK/client stack just for advice: rejected because it adds unnecessary abstraction for the MVP.

## Decision 2: Add a freeform advisory response path instead of reusing the bot decision contract as-is

- **Decision**: Extend the current OpenAI integration with a separate freeform advisory response shape rather than forcing conversational answers into the existing `selectedOptionId` response contract.
- **Rationale**: Bot decisions are structured option picks. The advice sidebar needs natural-language responses, failure messaging, and multi-turn conversation support.
- **Alternatives considered**:
  - Reuse `selectedOptionId` by wrapping every answer as an option: rejected because it is unnatural for chat and would complicate prompt/response parsing.
  - Create a totally separate provider service unrelated to the current client: rejected because only the response shape differs materially.

## Decision 3: Make advice requests user-initiated from the Blazor Server board, not pushed automatically

- **Decision**: Generate advisory answers only when a player opens the chat or sends a message, rather than on every game-state change.
- **Rationale**: This keeps API usage proportional to player demand, reduces noise, and avoids background advice churn during active multiplayer updates.
- **Alternatives considered**:
  - Precompute/push advice after every state update: rejected because it increases cost and complexity without being required by the feature spec.
  - Add a new SignalR hub for advice delivery: rejected because Blazor Server already gives the component a server-executed request path.

## Decision 4: Refresh authoritative context on every submitted question

- **Decision**: Build the advisory context from the latest authoritative game state and current seat-control context at send time for each user question.
- **Rationale**: The board can change between messages. Per-question refresh avoids stale answers and upholds the constitution rule that advisory outputs are derived from authoritative state.
- **Alternatives considered**:
  - Snapshot context once when the drawer opens: rejected because it would go stale during live multiplayer play.
  - Trust only the component's cached state: rejected because the board already has separate authoritative refresh paths for some derived data.

## Decision 5: Keep conversation history session-scoped for MVP

- **Decision**: Retain the visible conversation only for the current board page session and do not persist advisor transcripts to Azure Table storage in the MVP.
- **Rationale**: The spec requires visible conversation history during the current page session, not durable replay. Avoiding persistence keeps the first version simpler and avoids designing a new storage/indexing model prematurely.
- **Alternatives considered**:
  - Persist transcripts per game in table storage: rejected because it adds durability, privacy, and concurrency concerns that the spec does not require.
  - Discard every message after each answer: rejected because it would violate the requirement to preserve the visible conversation thread.

## Decision 6: Use a floating lower-right entry point plus a MudBlazor drawer/sidebar

- **Decision**: Add a floating advisor icon in the lower-right board area that opens a right-side chat drawer/sidebar implemented with MudBlazor layout components.
- **Rationale**: This matches the requested interaction, keeps the main board visible, and avoids a larger three-column layout refactor.
- **Alternatives considered**:
  - Add advice as another analysis tab: rejected because the user explicitly asked for a chat icon and sidebar experience.
  - Replace the existing analysis panel with chat: rejected because it would displace current map/stat/history tools.

## Decision 7: Tailor advice from the controlled-seat context, not just the active player

- **Decision**: Advisory context is built for the seat the current user is directly controlling or delegated to control, falling back to the most relevant session seat when only one applies.
- **Rationale**: The spec asks for advice tied to the player's strategy. In Boxcars, a user may act for a delegated seat, so the relevant strategy context is the controlled seat, not necessarily the authenticated identity alone.
- **Alternatives considered**:
  - Always advise only the active player: rejected because observers and delegated controllers can need context for a different seat.
  - Always advise from the current authenticated user's original seat: rejected because delegated control would produce misleading answers.

## Decision 8: Advice must remain explicitly non-authoritative

- **Decision**: The advisor will label answers as guidance and will never be positioned as a rules arbiter or automatic action source.
- **Rationale**: The constitution requires advisory projections to remain derived and non-decisive. This is especially important when advice discusses route, fee, and purchase strategy.
- **Alternatives considered**:
  - Let the advisor present itself as the "best move": rejected because it overstates confidence and conflicts with server authority.
  - Allow advice to trigger actions directly: rejected because the feature spec requests chat help, not autonomous gameplay.