# Contract: Dashboard Bot Library

## Purpose

Define the dashboard-facing contract for creating, listing, editing, and deleting global bot definitions shared across all users and games.

## Surface

- Dashboard page in `src/Boxcars/Components/Pages/Dashboard.razor`
- Backing application service in `src/Boxcars/Services/`
- Persistent storage in `BotsTable`

## Bot Definition Shape

```json
{
  "botDefinitionId": "bot-iron-strategist",
  "name": "Iron Strategist",
  "strategyText": "Favor high-value routes and preserve monopoly leverage.",
  "createdByUserId": "user-123",
  "createdUtc": "2026-03-16T18:20:00Z",
  "modifiedByUserId": "user-123",
  "modifiedUtc": "2026-03-16T18:20:00Z",
  "etag": "W/\"datetime'2026-03-16T18%3A20%3A00.0000000Z'\""
}
```

## Read Contract

### Request

- Signed-in user opens the dashboard BOT management surface.

### Response

```json
{
  "bots": [
    {
      "botDefinitionId": "bot-iron-strategist",
      "name": "Iron Strategist",
      "strategyText": "Favor high-value routes and preserve monopoly leverage.",
      "modifiedUtc": "2026-03-16T18:20:00Z",
      "etag": "W/\"datetime'2026-03-16T18%3A20%3A00.0000000Z'\""
    }
  ],
  "canManageBots": true
}
```

## Create Contract

### Input

```json
{
  "name": "Iron Strategist",
  "strategyText": "Favor high-value routes and preserve monopoly leverage."
}
```

### Rules

- `name` is required and trimmed.
- `strategyText` may be blank.
- Creator must be authenticated.

### Success Output

- Returns the created `BotStrategyDefinition` including generated ID and ETag.

## Update Contract

### Input

```json
{
  "botDefinitionId": "bot-iron-strategist",
  "name": "Iron Strategist v2",
  "strategyText": "Prioritize reach and avoid overpaying.",
  "etag": "W/\"datetime'2026-03-16T18%3A20%3A00.0000000Z'\""
}
```

### Rules

- Update requires the latest ETag.
- On ETag mismatch, the update fails with a concurrency conflict and no silent overwrite occurs.
- Successful update changes future assignment behavior immediately because gameplay resolves assignments by bot-definition ID at decision time.

### Conflict Output

```json
{
  "code": "BotDefinitionConflict",
  "message": "This bot was updated by another user. Reload the latest definition before saving again."
}
```

## Delete Contract

### Input

```json
{
  "botDefinitionId": "bot-iron-strategist",
  "etag": "W/\"datetime'2026-03-16T18%3A25%3A00.0000000Z'\""
}
```

### Rules

- Delete removes the definition from future assignment choices.
- Any active gameplay assignment referencing the deleted definition becomes invalid on the next resolution attempt.

## Behavioral Guarantees

- Any signed-in user can manage the global bot library.
- The library is shared across all users and games.
- Dashboard CRUD must use optimistic concurrency so concurrent changes are explicit.
- The dashboard contract does not expose or persist per-game assignment state.