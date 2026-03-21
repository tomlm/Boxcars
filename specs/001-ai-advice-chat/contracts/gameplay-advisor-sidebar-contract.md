# Contract: Gameplay Advisor Sidebar

## Purpose

Define the user-visible contract for the in-game AI advisor entry point, sidebar conversation behavior, and retry/failure states.

## Surface

- Game board page in `src/Boxcars/Components/Pages/GameBoard.razor`
- Advisor UI components under `src/Boxcars/Components/GameBoard/`
- Advisory orchestration service under `src/Boxcars/Services/`

## Entry Point Contract

The game board exposes a lower-right advisor affordance that remains discoverable while the board is in view.

```json
{
  "isVisible": true,
  "placement": "lower-right",
  "action": "open-advisor-sidebar"
}
```

### Guarantees

- Activating the advisor does not navigate away from the board.
- The board remains visible and playable while the advisor is open.
- The advisor can be dismissed without affecting gameplay state.

## Opening Behavior Contract

When the advisor is opened for the current board page session, it seeds the conversation with the fixed greeting.

```json
{
  "messages": [
    {
      "role": "assistant",
      "content": "How can I help?"
    }
  ]
}
```

## Message Submission Contract

### Input

```json
{
  "gameId": "game-42",
  "userMessage": "Should I buy this railroad or hold cash?"
}
```

### Behavioral guarantees

- The server refreshes authoritative advisory context before answering.
- The answer is framed as strategy guidance, not an authoritative game result.
- The visible conversation transcript remains intact for the current page session.

### Success Output

```json
{
  "succeeded": true,
  "contextTurnNumber": 18,
  "assistantMessage": "You are cash-tight this turn, so holding cash is safer unless the railroad materially improves your access into the next destination regions."
}
```

### Failure Output

```json
{
  "succeeded": false,
  "failureReason": "Advisor is temporarily unavailable. Try again in a moment."
}
```

## State Refresh Contract

- Every submitted question uses the latest authoritative game snapshot available at send time.
- If the board changed between two messages, the second answer reflects the newer state.
- Opening the sidebar alone does not commit gameplay actions or alter authoritative game state.

## Availability Contract

- Missing configuration, provider failure, or timeout surfaces a retryable user-facing failure message.
- Failure to generate advice does not block turn progression or board rendering.

## Responsive UI Contract

- Desktop layout keeps the map and player surfaces usable while the advisor is open.
- Mobile layout keeps the advisor readable, dismissible, and non-destructive to the board session.