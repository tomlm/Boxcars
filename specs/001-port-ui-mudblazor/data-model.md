# Data Model: UI Component Library Migration

## Entity: UI Surface
Represents a user-facing page or reusable component that participates in migration.

### Fields
- `SurfaceId` (string): Unique identifier (usually file path-derived)
- `Name` (string): Human-readable surface name
- `Location` (string): Repository path
- `SurfaceType` (enum): `Page` | `Layout` | `ReusableComponent`
- `MigrationStatus` (enum): `NotStarted` | `InProgress` | `Migrated` | `Validated`
- `ParityStatus` (enum): `Unknown` | `Pass` | `Fail`
- `ResponsiveStatus` (enum): `Unknown` | `Pass` | `Fail`
- `LegacyUsageDetected` (bool): Whether legacy control usage is still present

### Validation Rules
- `Location` must point to a file under `src/Boxcars/Components` or root composition files.
- `MigrationStatus = Validated` requires `ParityStatus = Pass`, `ResponsiveStatus = Pass`, and `LegacyUsageDetected = false`.

### State Transitions
- `NotStarted -> InProgress -> Migrated -> Validated`
- Any failed check can move `Validated -> InProgress` for remediation.

## Entity: Control Mapping
Represents a concrete mapping from legacy control pattern to target control pattern.

### Fields
- `MappingId` (string): Unique mapping key
- `LegacyPattern` (string): Legacy control/pattern signature
- `TargetPattern` (string): Replacement control/pattern signature
- `BehaviorNotes` (string): Expected interaction/visual parity notes
- `CoverageCount` (int): Number of surfaces using this mapping
- `Approved` (bool): Whether mapping is accepted for production use

### Validation Rules
- `LegacyPattern` and `TargetPattern` must both be non-empty.
- `Approved = true` requires at least one validated usage with parity pass.

## Entity: Style Rule Set
Represents custom styles associated with a migrated surface.

### Fields
- `RuleSetId` (string): Unique identifier
- `SurfaceId` (string): Foreign key to `UI Surface`
- `PreMigrationRuleCount` (int)
- `PostMigrationRuleCount` (int)
- `RemainingRuleRationale` (string): Why remaining rules are required
- `UsesThemeTokens` (bool): Whether theme/system tokens are used

### Validation Rules
- `PostMigrationRuleCount` must be less than or equal to `PreMigrationRuleCount`.
- If `PostMigrationRuleCount > 0`, `RemainingRuleRationale` must be non-empty.

## Relationships
- One `UI Surface` can reference many `Control Mapping` entries.
- One `UI Surface` has one `Style Rule Set` summary for migration tracking.
