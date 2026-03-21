# Data Model: AI Advice Chat

## Entity Overview

```text
AdvisorEntryPointState (session-scoped UI)
        │
        ├── opens ───────────────> AdvisorConversationSession (session-scoped)
        │                                │
        │                                ├── contains ─────> AdvisorMessage (session-scoped)
        │                                └── requests ─────> AdvisorContextSnapshot (transient)
        │                                                        │
        │                                                        └── produces ──> AdvisorResponse (transient)
        │
        └── reflects ───────────> Availability / Loading / Failure state
```

## AdvisorEntryPointState

**Purpose**: Tracks whether the game board advisor affordance is collapsed or open and whether a request is active.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Current game board identifier. |
| `IsOpen` | `bool` | Whether the advisor sidebar is visible. |
| `IsLoading` | `bool` | Whether an advice request is in flight. |
| `HasAvailabilityError` | `bool` | Whether the advisor is currently unavailable. |
| `AvailabilityMessage` | `string?` | User-facing explanation when advice cannot be generated. |

**Validation rules**:

- `IsLoading` may be true only while the sidebar is open.
- `AvailabilityMessage` is populated only when the advisor cannot answer.

## AdvisorConversationSession

**Purpose**: Holds the visible advisory conversation for the current board page session.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Owning game identifier. |
| `CurrentUserId` | `string` | Authenticated user for the board session. |
| `ControlledPlayerIndex` | `int?` | Seat index whose strategy context is being used. |
| `StartedUtc` | `DateTimeOffset` | Time the visible conversation started. |
| `LastContextRefreshUtc` | `DateTimeOffset?` | Time the latest authoritative context was assembled. |
| `Messages` | `IReadOnlyList<AdvisorMessage>` | Ordered visible transcript for the session. |

**Validation rules**:

- The first assistant message is always the fixed greeting.
- Conversation history is not persisted beyond the current board page session in the MVP.

## AdvisorMessage

**Purpose**: A single visible chat item in the sidebar transcript.

| Field | Type | Description |
|---|---|---|
| `MessageId` | `string` | Stable message identifier for UI rendering. |
| `Role` | `string` | `Assistant` or `User`. |
| `Content` | `string` | Rendered message text. |
| `CreatedUtc` | `DateTimeOffset` | Timestamp for ordering/display. |
| `ContextTurnNumber` | `int?` | Turn number used for the associated advisory context, when applicable. |

## AdvisorContextSnapshot

**Purpose**: The transient authoritative context payload assembled before each AI request.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Current game identifier. |
| `TurnNumber` | `int` | Current authoritative turn number. |
| `TurnPhase` | `string` | Current authoritative phase. |
| `ActivePlayerIndex` | `int` | Current active seat. |
| `ControlledPlayerIndex` | `int?` | Seat used for advice and strategy context. |
| `ControlledPlayerName` | `string` | Player whose strategy is being discussed. |
| `ControlledPlayerSummary` | `string` | Summary of cash, engine, destination, fee pressure, and rail ownership. |
| `OtherPlayerSummaries` | `IReadOnlyList<string>` | Concise summaries of opponents relevant to strategy. |
| `BoardSituationSummary` | `string` | Immediate board/phase context relevant to the question. |
| `RecentConversation` | `IReadOnlyList<AdvisorMessage>` | Prior visible messages included for continuity. |

**Validation rules**:

- The snapshot must be rebuilt from the latest authoritative state for every submitted user question.
- Snapshot content is advisory only and must not become a mutation source.

## AdvisorResponse

**Purpose**: The transient result returned by the advice-generation service.

| Field | Type | Description |
|---|---|---|
| `Succeeded` | `bool` | Whether advice text was produced successfully. |
| `AssistantText` | `string?` | Final advisory reply shown to the user. |
| `FailureReason` | `string?` | User-safe failure explanation when generation fails. |
| `ContextTurnNumber` | `int?` | Turn number used for the answer. |
| `CompletedUtc` | `DateTimeOffset` | Response timestamp. |

## State Transitions

### Sidebar lifecycle

```text
Collapsed
  -> OpenIdle
OpenIdle
  -> Sending
  -> Collapsed
Sending
  -> OpenAnswered
  -> OpenFailed
OpenAnswered
  -> Sending
  -> Collapsed
OpenFailed
  -> Sending
  -> Collapsed
```

### Conversation lifecycle

```text
NoSession
  -> GreetingSeeded
GreetingSeeded
  -> UserMessageAdded
UserMessageAdded
  -> AssistantReplyAdded
  -> FailureShown
AssistantReplyAdded
  -> UserMessageAdded
FailureShown
  -> UserMessageAdded
```