# Contract: Runtime Game Settings Resolution

## Purpose

Define how persisted game settings are resolved and applied across the authoritative engine, board projections, advisory text, and legacy game fallback.

## Surface

- Game loading/orchestration in `src/Boxcars/GameEngine/GameEngineService.cs`
- Engine domain rules in `src/Boxcars.Engine/Domain/GameEngine.cs`
- Board state mapping in `src/Boxcars/Services/GameBoardStateMapper.cs`
- Advice/projection services in `src/Boxcars/Services/GameBoardAdviceService.cs`
- Cash display models in `src/Boxcars/Data/PlayerBoardModel.cs`

## Resolved Settings Shape

```json
{
  "source": "Persisted | LegacyDefaulted | PartiallyDefaulted",
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

## Resolution Rules

- Runtime settings are resolved from the owning direct `GameEntity` setting columns.
- If persisted columns are missing or null, the resolver applies the documented defaults before gameplay or projection code uses the settings.
- Request/runtime contracts use `schemaVersion`; persisted `GameEntity` rows store the same value in `settingsSchemaVersion`.
- The resolved settings object is immutable for the lifetime of the loaded game.
- Authoritative gameplay rules and board/advisory projections must consume the same resolved settings object or a projection derived directly from it.

## Rule Application Guarantees

- Opening cash uses `startingCash`.
- Declaration/win checks use `winningCash`.
- Cash visibility uses `keepCashSecret` and `announcingCash`.
- Rover awards use `roverCash`.
- Route fees use `publicFee`, `privateFee`, `unfriendlyFee1`, and `unfriendlyFee2`.
- Starting locomotives use `startEngine`.
- Engine upgrades use `expressPrice` and `superchiefPrice`.
- Home-selection and swap behavior use `homeCityChoice` and `homeSwapping`.

## Legacy Compatibility Contract

If a game was created before this feature and its direct game-setting columns are absent or null:

- The game still loads successfully.
- Missing values resolve to the documented defaults.
- A missing `settingsSchemaVersion` resolves to runtime `schemaVersion = 1`.
- The resolved settings source is treated as `LegacyDefaulted` for diagnostics/testing.

## Cash Visibility Contract

When `keepCashSecret` is `true`:

- A player always sees their own exact cash.
- Opponents see concealed cash while the player's cash is below `announcingCash`.
- Opponents see exact cash while the player's cash is at or above `announcingCash`.
- If the player's cash later drops below `announcingCash`, the opponent view returns to concealed cash.

When `keepCashSecret` is `false`:

- All players can see exact cash amounts at all times.

## Behavioral Guarantees

- Runtime projections must not keep separate hard-coded thresholds or fee tables.
- If a projection disagrees with engine rule resolution, the engine rule resolution wins and the projection must be corrected.
- No client-only settings state is authoritative after game creation succeeds.
