# Data Model: Game Creation Settings

## Entity Overview

```text
CreateGameRequest
        │
        └── contains ───────> GameSettingsInput
                                   │
                                   ├── validates into ──> GameSettings
                                   │                           │
                                   │                           ├── persisted in ──> GameRecord direct columns
                                   │                           ├── resolved as ───> ResolvedGameSettings
                                   │                           └── consumed by ───> AuthoritativeGameRules
                                   │                                                    │
                                   │                                                    ├── gameplay rule checks
                                   │                                                    ├── board projections
                                   │                                                    └── advisory UI text
                                   └── defaults from ───> GameSettingsDefaults
```

## GameSettings

**Purpose**: Immutable per-game ruleset chosen during game creation and used throughout the life of the game.

| Field | Type | Description |
|---|---|---|
| `StartingCash` | `int` | Opening cash for every player. Default `20000`. |
| `AnnouncingCash` | `int` | Cash threshold at or above which exact cash becomes public when `KeepCashSecret` is enabled. Default `250000`. |
| `WinningCash` | `int` | Cash threshold required for declaration/win checks. Default `300000`. |
| `RoverCash` | `int` | Award for roving a declared player. Default `50000`. |
| `PublicFee` | `int` | Fee for public railroad travel. Default `1000`. |
| `PrivateFee` | `int` | Fee when the route uses the rider's own railroad. Default `1000`. |
| `UnfriendlyFee1` | `int` | Unfriendly-owner fee before all sellable railroads are sold. Default `5000`. |
| `UnfriendlyFee2` | `int` | Unfriendly-owner fee after all sellable railroads are sold. Default `10000`. |
| `HomeSwapping` | `bool` | Whether a player may swap home and first destination after both are chosen. Default `true`. |
| `HomeCityChoice` | `bool` | Whether players choose a city within the selected home region instead of receiving a random city. Default `true`. |
| `KeepCashSecret` | `bool` | Whether opponents see concealed cash below `AnnouncingCash`. Default `true`. |
| `StartEngine` | `string` / enum | Starting locomotive for all players. Allowed values: `Freight`, `Express`, `Superchief`. Default `Freight`. |
| `SuperchiefPrice` | `int` | Upgrade cost to `Superchief`. Default `40000`. |
| `ExpressPrice` | `int` | Upgrade cost to `Express`. Default `4000`. |
| `SchemaVersion` | `int` | Canonical runtime/request schema marker for settings payload evolution. Initial value `1`. This maps to persisted `SettingsSchemaVersion`. |

**Validation rules**:

- All numeric fields are required positive whole-dollar values.
- `WinningCash >= AnnouncingCash`.
- `StartEngine` must be `Freight`, `Express`, or `Superchief`.
- `HomeSwapping`, `HomeCityChoice`, and `KeepCashSecret` are explicit booleans.

## GameSettingsInput

**Purpose**: Create-game request payload carrying user-selected or defaulted settings before server validation.

| Field | Type | Description |
|---|---|---|
| `Settings` | `GameSettings`-shaped object | Candidate rules submitted during game creation. |
| `WasCustomized` | `bool` | Optional UI helper flag indicating whether the user changed defaults. Not required for persistence. |

**Behavior**:

- Client initializes the input from `GameSettingsDefaults`.
- Server revalidates and normalizes the payload before persistence.
- Invalid input blocks game creation.

## GameRecord

**Purpose**: Durable storage row for a game, including immutable settings.

| Field | Type | Description |
|---|---|---|
| `GameId` | `string` | Stable game identifier. |
| `CreatorId` | `string` | User who created the game. |
| `MapFileName` | `string` | Selected map asset. |
| `MaxPlayers` | `int` | Player count. |
| `CurrentPlayerCount` | `int` | Current seat count. |
| `CreatedAt` | `DateTimeOffset` | Game creation timestamp. |
| `StartingCash` | `int?` | Persisted opening cash value. Nullable for legacy-row fallback. |
| `AnnouncingCash` | `int?` | Persisted public-cash threshold. Nullable for legacy-row fallback. |
| `WinningCash` | `int?` | Persisted declaration/win threshold. Nullable for legacy-row fallback. |
| `RoverCash` | `int?` | Persisted rover award. Nullable for legacy-row fallback. |
| `PublicFee` | `int?` | Persisted public-railroad fee. Nullable for legacy-row fallback. |
| `PrivateFee` | `int?` | Persisted own-railroad fee. Nullable for legacy-row fallback. |
| `UnfriendlyFee1` | `int?` | Persisted unfriendly fee before all sellable railroads are sold. Nullable for legacy-row fallback. |
| `UnfriendlyFee2` | `int?` | Persisted unfriendly fee after all sellable railroads are sold. Nullable for legacy-row fallback. |
| `HomeSwapping` | `bool?` | Persisted home/destination swap toggle. Nullable for legacy-row fallback. |
| `HomeCityChoice` | `bool?` | Persisted home-city choice toggle. Nullable for legacy-row fallback. |
| `KeepCashSecret` | `bool?` | Persisted cash secrecy toggle. Nullable for legacy-row fallback. |
| `StartEngine` | `string?` | Persisted starting locomotive. Nullable for legacy-row fallback. |
| `SuperchiefPrice` | `int?` | Persisted `Superchief` upgrade price. Nullable for legacy-row fallback. |
| `ExpressPrice` | `int?` | Persisted `Express` upgrade price. Nullable for legacy-row fallback. |
| `SettingsSchemaVersion` | `int?` | Optional persisted schema marker for future evolution. Nullable for legacy-row fallback. This is the storage column corresponding to runtime/request `SchemaVersion`. |

**Storage mapping**:

| Property | Value |
|---|---|
| Table | `GamesTable` |
| PartitionKey | `GameId` |
| RowKey | `GAME` |

**Schema version mapping**:

- Create-game requests and resolved runtime settings use `SchemaVersion`.
- `GameEntity` persists the same value in `SettingsSchemaVersion`.
- Resolver logic treats `SettingsSchemaVersion == null` as a legacy row and defaults the runtime `SchemaVersion` to `1`.

## ResolvedGameSettings

**Purpose**: Server-side canonical rules object used at runtime after reading direct `GameEntity` columns and applying legacy fallback.

| Field | Type | Description |
|---|---|---|
| `Settings` | `GameSettings` | Fully populated rules object with defaults applied. |
| `Source` | `string` | `Persisted`, `LegacyDefaulted`, or `PartiallyDefaulted`. |
| `Warnings` | `IReadOnlyList<string>` | Optional diagnostics for logging/telemetry when payloads are incomplete or outdated. |

**Behavior**:

- Used when creating a new engine instance.
- Used when restoring an engine from snapshots/events.
- Used by board/advice mapping so projections and engine logic stay aligned.

## GameSettingsDefaults

**Purpose**: Single definition of default values for new games and legacy fallback.

| Field | Type | Description |
|---|---|---|
| `DefaultSettings` | `GameSettings` | Canonical initial default payload. |

**Behavior**:

- Applied when the create-game page initializes.
- Applied when persisted `GameEntity` setting columns are missing or null.
- Used in tests to assert legacy compatibility.

## CashVisibilityProjection

**Purpose**: Transient view-model calculation controlling what another player sees for cash.

| Field | Type | Description |
|---|---|---|
| `CanViewExactCash` | `bool` | Whether the viewer can see exact cash. |
| `DisplayMode` | `string` | `Exact` or `Concealed`. |
| `ThresholdApplied` | `int` | The `AnnouncingCash` value used in the calculation. |

**Behavior**:

- Current user still sees exact cash for their own seat.
- Opponents see exact cash only when `KeepCashSecret == false` or the player's cash is `>= AnnouncingCash`.
- If cash later drops below `AnnouncingCash`, display returns to concealed mode.

## HomeSelectionRuleState

**Purpose**: Transient authoritative state for pre-start home-selection behavior.

| Field | Type | Description |
|---|---|---|
| `PlayerIndex` | `int` | Active player choosing or receiving a home city. |
| `SelectedRegionCode` | `string` | Home region being resolved. |
| `EligibleCityNames` | `IReadOnlyList<string>` | Cities available when `HomeCityChoice` is enabled. |
| `AlreadyClaimedHomeCities` | `IReadOnlyList<string>` | Cities blocked because another player already owns them. |
| `SwapAvailable` | `bool` | Whether `HomeSwapping` can currently be offered after first destination selection. |

## State Transitions

### Settings lifecycle

```text
DefaultsLoaded
  -> EditedInCreateFlow
  -> AcceptedAsDefaults
EditedInCreateFlow
  -> ServerValidated
ServerValidated
  -> PersistedImmutable
PersistedImmutable
  -> ResolvedAtRuntime
ResolvedAtRuntime
  -> LegacyDefaulted (only for older games with missing/null setting columns)
```

### Cash visibility lifecycle

```text
Concealed
  -> ExactVisibleWhenThresholdReached
ExactVisibleWhenThresholdReached
  -> ConcealedWhenCashDropsBelowThreshold
```

### Home setup lifecycle

```text
HomeRegionPending
  -> HomeCityRandomized        (HomeCityChoice = false)
  -> HomeCitySelectable        (HomeCityChoice = true)
HomeCitySelectable
  -> HomeAssigned
HomeAssigned
  -> HomeSwapOffered           (HomeSwapping = true and first destination selected)
```
