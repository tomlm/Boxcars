# Contract: Advice Generation Context And Response

## Purpose

Define the internal service contract between the game board advisory orchestrator and the freeform AI response path.

## Surface

- Advisory service in `src/Boxcars/Services/`
- Existing OpenAI client/configuration in `src/Boxcars/Services/OpenAiBotClient.cs`
- Authoritative board state assembly from `src/Boxcars/Services/GameBoardStateMapper.cs`, `src/Boxcars/GameEngine/GameEngineService.cs`, and game/player state services

## Request Context Shape

```json
{
  "gameId": "game-42",
  "turnNumber": 18,
  "turnPhase": "Purchase",
  "activePlayerIndex": 1,
  "controlledPlayerIndex": 1,
  "controlledPlayerName": "Alice",
  "controlledPlayerSummary": {
    "cash": 32000,
    "engine": "Express",
    "destinationCity": "Miami",
    "feePressure": 5000,
    "ownedRailroads": ["PRR", "B&O"]
  },
  "boardSituationSummary": "Alice is in purchase phase after arriving with moderate cash pressure and two owned railroads.",
  "recentConversation": [
    {
      "role": "assistant",
      "content": "How can I help?"
    },
    {
      "role": "user",
      "content": "Should I buy now?"
    }
  ],
  "userQuestion": "Should I buy now?"
}
```

## Request Guarantees

- Context is assembled from the latest authoritative state available at request time.
- Context includes the player strategy facts needed for advice but excludes secrets/configuration values.
- Context is advisory-only and is never used to mutate game state directly.

## Response Shape

```json
{
  "succeeded": true,
  "assistantText": "You can buy if the railroad meaningfully improves your reach, but your current cash and fee pressure suggest protecting operating flexibility first.",
  "contextTurnNumber": 18,
  "completedUtc": "2026-03-20T18:42:00Z"
}
```

## Failure Shape

```json
{
  "succeeded": false,
  "failureReason": "OpenAI request timed out.",
  "contextTurnNumber": 18,
  "completedUtc": "2026-03-20T18:42:15Z"
}
```

## Behavioral Guarantees

- The service returns freeform assistant text and does not require a `selectedOptionId`.
- Provider failures, parse failures, and timeouts are converted into safe user-facing failures.
- The advice service does not enqueue actions, update player state rows, or broadcast gameplay mutations.