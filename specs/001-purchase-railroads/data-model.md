# Data Model: Purchase Phase Buying and Map Analysis

## Entity: PurchasePhaseModel
- **Purpose**: UI-facing state for the active player's purchase phase.
- **Fields**:
  - `PlayerIndex` (int, required)
  - `PlayerName` (string, required)
  - `CashAvailable` (int, required)
  - `DestinationCityName` (string, required)
  - `PayoutAmount` (int, required)
  - `CashAfterPayout` (int, required)
  - `RailroadOptions` (collection of `RailroadPurchaseOption`)
  - `EngineOptions` (collection of `EngineUpgradeOption`)
  - `TaskbarOptions` (collection of `PurchaseOptionModel`)
  - `CanDecline` (bool, required)
  - `CurrentCoverage` (`NetworkCoverageSnapshot`, optional)
  - `ProjectedCoverage` (`NetworkCoverageSnapshot`, optional)
  - `SelectedTab` (enum, required: `Map`, `Information`)
  - `MapAnalysisReport` (`MapAnalysisReport`, optional)
  - `SelectedOptionKey` (string, optional)
  - `SelectedRailroadOverlay` (`RailroadOverlayInfo`, optional)
  - `HasActivePurchaseControls` (bool, required)
  - `NoPurchaseNotification` (string, optional)
- **Validation**:
  - `HasActivePurchaseControls = true` requires at least one railroad or engine option.
  - `NoPurchaseNotification` is present only when purchasable entities exist in the game but none are affordable.

## Entity: RailroadPurchaseOption
- **Purpose**: A buyable railroad option shown in the purchase phase.
- **Fields**:
  - `RailroadIndex` (int, required)
  - `RailroadName` (string, required)
  - `ShortName` (string, optional)
  - `PurchasePrice` (int, required)
  - `IsAffordable` (bool, required; expected to be `true` for displayed options)
  - `IsSelected` (bool, required)
- **Validation**:
  - Railroad must be unowned and non-public.
  - Purchase price must match the authoritative railroad purchase price in the engine/map state.

## Entity: EngineUpgradeOption
- **Purpose**: A buyable locomotive upgrade option shown in the purchase phase.
- **Fields**:
  - `EngineType` (enum, required: `Express` or `Superchief`)
  - `DisplayName` (string, required)
  - `PurchasePrice` (int, required)
  - `CurrentEngineType` (enum, required)
  - `IsEligible` (bool, required)
  - `IsSelected` (bool, required)
- **Validation**:
  - Options only appear for forward upgrades from the player's current engine type.
  - `Express` always prices at $4000.
  - `Superchief` price comes from typed game settings.

## Entity: NetworkCoverageSnapshot
- **Purpose**: Purchase-analysis summary of the active player's railroad network.
- **Fields**:
  - `AccessibleCityCount` (int, required)
  - `TotalCityCount` (int, required)
  - `AccessibleCityPercent` (decimal, required)
  - `MonopolyCityCount` (int, required)
  - `MonopolyCityPercent` (decimal, required)
- **Validation**:
  - Percentages derive from counts and total city count.
  - Percentages are bounded to 0–100 inclusive.

## Entity: PurchaseDecision
- **Purpose**: The active player’s single purchase-phase selection before confirmation.
- **Fields**:
  - `OptionKind` (enum, required: `Railroad`, `EngineUpgrade`)
  - `OptionKey` (string, required)
  - `AmountPaid` (int, required)
  - `CanConfirm` (bool, required)
- **Validation**:
  - Only one decision may exist at a time.
  - `CanConfirm` is true only when the selected option is still valid and affordable.

## Entity: PurchaseTaskbarState
- **Purpose**: Inline purchase controls shown during the purchase phase.
- **Fields**:
  - `Label` (string, required; `Options`)
  - `Options` (collection of `PurchaseOptionModel`)
  - `CanBuy` (bool, required)
  - `CanDecline` (bool, required)

## Entity: PurchaseOptionModel
- **Purpose**: One taskbar purchase entry used by the synchronized combobox.
- **Fields**:
  - `OptionKey` (string, required)
  - `OptionKind` (enum, required: `Railroad`, `EngineUpgrade`)
  - `DisplayName` (string, required)
  - `PurchasePrice` (int, required)
  - `SortPriceDescendingKey` (int, required)
  - `IsSelected` (bool, required)

## Entity: RailroadOverlayInfo
- **Purpose**: Inline map overlay details shown for the currently selected railroad.
- **Fields**:
  - `RailroadIndex` (int, required)
  - `RailroadName` (string, required)
  - `PurchasePrice` (int, required)
  - `AccessChangePercent` (decimal, required)
  - `MonopolyChangePercent` (decimal, required)

## Entity: MapAnalysisReport
- **Purpose**: Structured report derived from the loaded map and shown on the Information tab.
- **Fields**:
  - `MapName` (string, required)
  - `GeneratedAtUtc` (datetime, required)
  - `RailroadRows` (collection of `RailroadAnalysisRow`)
  - `CityAccessRows` (collection of `CityAccessRow`)
  - `RegionProbabilityRows` (collection of `RegionProbabilityRow`)
  - `AverageTripLengthDots` (decimal, required)
  - `AveragePayoff` (decimal, required)
  - `AveragePayoffPerDot` (decimal, required)

## Entity: RailroadAnalysisRow
- **Purpose**: Summary of one railroad's map-derived characteristics.
- **Fields**:
  - `RailroadCode` (string, required)
  - `FullName` (string, required)
  - `PurchasePrice` (int, required)
  - `CitiesServedCount` (int, required)
  - `ServicePercentage` (decimal, required)
  - `MonopolyPercentage` (decimal, required)
  - `ConnectionCount` (int, required)

## Entity: CityAccessRow
- **Purpose**: One city's destination-frequency summary used in the Information tab report.
- **Fields**:
  - `RegionCode` (string, required)
  - `CityName` (string, required)
  - `AccessPercentage` (decimal, required)

## Entity: RegionProbabilityRow
- **Purpose**: Region-level destination probability summary.
- **Fields**:
  - `RegionCode` (string, required)
  - `RegionName` (string, required)
  - `ProbabilityPercentage` (decimal, required)

## Entity: RecommendationInputSet
- **Purpose**: Machine-readable analysis payload used by recommendation logic.
- **Fields**:
  - `MapAnalysisReport` (`MapAnalysisReport`, required)
  - `AffordableRailroadIndices` (collection of int)
  - `EligibleEngineTypes` (collection of enum)
  - `CurrentCoverage` (`NetworkCoverageSnapshot`, optional)
  - `ProjectedCoverageByRailroad` (dictionary keyed by railroad index)

## Entity: PurchaseExperienceTab
- **Purpose**: Represents the active purchase-phase tab in the UI.
- **Values**:
  - `Map`
  - `Information`

## Entity: PurchaseRulesOptions
- **Purpose**: Bound configuration values that affect purchase-phase pricing.
- **Fields**:
  - `SuperchiefPrice` (int, required, default 40000)
- **Validation**:
  - Value must be positive.
  - Value must be available to both engine validation and UI display.

## Relationships
- One `PurchasePhaseModel` has many `RailroadPurchaseOption` items.
- One `PurchasePhaseModel` has many `EngineUpgradeOption` items.
- One `PurchasePhaseModel` has many `PurchaseOptionModel` taskbar entries.
- One `PurchasePhaseModel` has one `PurchaseTaskbarState`.
- One `PurchasePhaseModel` has at most one `PurchaseDecision`.
- One selected `RailroadPurchaseOption` may produce one projected `NetworkCoverageSnapshot`.
- One selected `RailroadPurchaseOption` may produce one `RailroadOverlayInfo`.
- One `PurchasePhaseModel` has one `MapAnalysisReport` when analysis succeeds.
- One `RecommendationInputSet` reuses one `MapAnalysisReport`.
- `PurchaseRulesOptions` influences all `EngineUpgradeOption` prices and engine upgrade validation.

## State Transitions
- `ArrivalResolved` → `PurchasePhaseReady`
- `PurchasePhaseReady` → `PurchaseSkippedForNoEligibleOptions`
- `PurchasePhaseReady` → `PurchaseControlsActive`
- `PurchaseControlsActive(Map)` ↔ `PurchaseControlsActive(Information)`
- `RailroadSelectedOnMap` ↔ `RailroadSelectedInTaskbar`
- `PurchaseControlsActive` → `RailroadSelected`
- `PurchaseControlsActive` → `EngineUpgradeSelected`
- `RailroadSelected` → `PurchaseCommitted`
- `EngineUpgradeSelected` → `PurchaseCommitted`
- `PurchaseControlsActive` → `PurchaseDeclined`
- `PurchaseCommitted` → `UseFeesResolved`
- `PurchaseDeclined` → `UseFeesResolved`
- `PurchaseSkippedForNoEligibleOptions` → `UseFeesResolved`
