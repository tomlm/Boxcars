# Contract: Create Game Settings

## Purpose

Define the create-game contract for collecting, validating, and persisting immutable per-game rule settings before gameplay begins.

## Surface

- Create-game UI in `src/Boxcars/Components/Pages/CreateGame.razor`
- Request models in `src/Boxcars/Data/GameCreationModels.cs`
- Application orchestration in `src/Boxcars/Services/GameService.cs`
- Authoritative creation path in `src/Boxcars/GameEngine/GameEngineService.cs`
- Persistent storage in `GamesTable` via direct `GameEntity` setting columns

## CreateGameRequest Shape

```json
{
  "creatorUserId": "user-123",
  "mapFileName": "U21MAP.RB3",
  "players": [
    {
      "userId": "user-123",
      "displayName": "Alice",
      "color": "red"
    },
    {
      "userId": "user-456",
      "displayName": "Bob",
      "color": "blue"
    }
  ],
  "settings": {
    "startingCash": 20000,
    "announcingCash": 250000,
    "winningCash": 300000,
    "roverCash": 50000,
    "publicFee": 1000,
    "privateFee": 1000,
    "unfriendlyFee1": 5000,
    "unfriendlyFee2": 10000,
    "homeSwapping": true,
    "homeCityChoice": true,
    "keepCashSecret": true,
    "startEngine": "Freight",
    "superchiefPrice": 40000,
    "expressPrice": 4000,
    "schemaVersion": 1
  }
}
```

## Rules

- `settings` is required for newly created games, even if the user accepts defaults.
- All numeric values must be positive integers.
- `winningCash` must be greater than or equal to `announcingCash`.
- `startEngine` must be one of `Freight`, `Express`, or `Superchief`.
- The request/runtime field name is `schemaVersion`; persistence stores the same value in `settingsSchemaVersion` on `GameEntity`.
- Player/color uniqueness rules remain unchanged from the current create-game flow.
- Server validation is authoritative; the UI may show validation earlier but cannot bypass it.

## Persistence Contract

On success, the game row stores the normalized settings values directly on `GameEntity` columns.

```json
{
  "startingCash": 20000,
  "announcingCash": 250000,
  "winningCash": 300000,
  "roverCash": 50000,
  "publicFee": 1000,
  "privateFee": 1000,
  "unfriendlyFee1": 5000,
  "unfriendlyFee2": 10000,
  "homeSwapping": true,
  "homeCityChoice": true,
  "keepCashSecret": true,
  "startEngine": "Freight",
  "superchiefPrice": 40000,
  "expressPrice": 4000,
  "settingsSchemaVersion": 1
}
```

## Failure Contract

Invalid settings reject game creation and leave no partially created game.

```json
{
  "success": false,
  "reason": "Winning cash must be greater than or equal to announcing cash."
}
```

## Behavioral Guarantees

- Every new game has a complete persisted settings column set on the owning game row.
- Settings are immutable after the game starts.
- The create-game page and the server use the same field set and default values.
- Request/runtime `schemaVersion` and persisted `settingsSchemaVersion` represent the same schema revision.
- Persisted settings belong to one game only and do not affect other games.
