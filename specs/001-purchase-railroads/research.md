# Research: Purchase Phase Buying and Map Analysis

## Decision 1: Keep purchase commits on the existing server-authoritative player-action pipeline
- **Decision**: Continue to commit railroad purchases and engine upgrades through `PurchaseRailroadAction`, `BuyEngineAction`, and `DeclinePurchaseAction` processed by `GameEngineService.ProcessTurn()`.
- **Rationale**: The current game architecture already validates turn ownership, phase correctness, and action persistence on the server. Reusing that path preserves multiplayer synchronization and minimizes feature risk.
- **Alternatives considered**:
  - Add a new dialog-specific purchase endpoint (rejected: duplicates authorization and turn validation logic).
  - Mutate client-side purchase state and reconcile later (rejected: violates server-authoritative multiplayer rules).

## Decision 2: Replace the stub with inline page-owned purchase controls, not a global dialog workflow
- **Decision**: Replace the purchase dialog concept with inline page-owned controls under `GameBoard.razor`: a taskbar combobox, BUY button, railroad overlay info, and synchronized map selection.
- **Rationale**: The current page already owns the arrival/purchase lifecycle, map state, and taskbar actions. Inline controls are simpler than a modal workflow and better match the user's goal of keeping the board continuously visible.
- **Alternatives considered**:
  - Convert the flow to a global `IDialogService` modal (rejected: adds indirection and makes map coordination harder).
  - Keep a movable purchase dialog (rejected: no longer matches the desired inline taskbar model).

## Decision 3: Offer synchronized purchase options in a high-to-low taskbar combobox
- **Decision**: The purchase UI exposes available railroad and engine purchase options in a taskbar combobox sorted by price from highest to lowest, with selection synchronized to the map.
- **Rationale**: This matches the clarified spec, reduces decision noise, and keeps the player out of a non-actionable dialog when nothing can be bought.
- **Alternatives considered**:
  - Keep the earlier ascending sort order (rejected: superseded by the latest UX amendment).
  - Require map-only selection without a synchronized list (rejected: weakens discoverability for engines and cross-checking prices).

## Decision 4: Compute network coverage from existing map graph data with a dedicated coverage service
- **Decision**: Add a focused service in `src/Boxcars/Services` that derives reachable-city and monopoly percentages from `MapDefinition`, railroad ownership, and hypothetical railroad additions.
- **Rationale**: The repo already has route/adjacency logic in `MapRouteService` and `GameEngine`. Reusing that graph knowledge keeps the algorithm local to the web layer and avoids adding new persisted graph models.
- **Alternatives considered**:
  - Persist precomputed network coverage in game state (rejected: premature denormalization and more invalidation paths).
  - Rebuild a separate graph domain model just for purchase analysis (rejected: unnecessary complexity).

## Decision 5: Treat engine upgrades as first-class taskbar options, but not map-highlighted selections
- **Decision**: Present engine upgrades in the same synchronized taskbar selector as railroads, with exactly one selectable purchase option at a time. Railroad selections drive map highlight and projected network stats; engine upgrades show price and resulting engine level without requiring map highlight.
- **Rationale**: This matches the clarified one-purchase-action rule while preserving the map-specific feedback only where it adds value.
- **Alternatives considered**:
  - Build a separate engine-upgrade dialog or second step (rejected: breaks the single purchase-phase UX).
  - Force engine upgrades through the same network-stat display pattern as railroads (rejected: no meaningful railroad-network delta to show).

## Decision 6: Move Superchief pricing into typed app settings and use the same value for UI and server validation
- **Decision**: Introduce typed purchase/game rules options bound from `appsettings*.json`, with Express fixed at $4000 and Superchief defaulting to $40000 unless configured otherwise.
- **Rationale**: The current code hardcodes upgrade prices in both `GameEngine` and `GameEngineService`, which conflicts with the spec and risks drift. A single bound configuration source keeps the server authoritative while making the rule adjustable.
- **Alternatives considered**:
  - Keep hardcoded prices and only change UI labels (rejected: would produce incorrect authoritative behavior).
  - Store the price per game instance in Azure Table Storage immediately (rejected: not required by the spec and adds persistence complexity).

## Decision 7: Push configurable engine pricing into engine construction/restoration, not only the web service layer
- **Decision**: Extend the engine creation/restoration path so pricing rules are available when `GameEngine.UpgradeLocomotive()` validates and applies an upgrade.
- **Rationale**: `UpgradeLocomotive()` currently deducts a hardcoded cost inside the domain model. To keep the engine authoritative, the configurable Superchief price must reach the engine instance rather than only being checked by `GameEngineService`.
- **Alternatives considered**:
  - Validate configurable pricing only in `GameEngineService` and let `GameEngine` keep hardcoded deductions (rejected: conflicting authoritative sources).
  - Move all upgrade pricing logic out of the engine into the UI/service layer (rejected: weakens domain invariants).

## Decision 8: Extend the existing purchase presentation model instead of introducing a second purchase-state source
- **Decision**: Replace the current minimal `ArrivalResolutionModel`-only purchase data with a richer purchase-phase view model built by `GameBoardStateMapper` from the current `RailBaronGameState`, price settings, coverage service output, synchronized selection state, and overlay information.
- **Rationale**: `GameBoard.razor` already consumes a mapped turn view state. Expanding that mapping preserves a single UI state source and keeps dialog rendering logic out of the page code-behind.
- **Alternatives considered**:
  - Query purchase data separately from the component after render (rejected: more lifecycle complexity and potential state drift).
  - Assemble all option lists directly in `GameBoard.razor` (rejected: makes the page more monolithic).

## Decision 9: Build a reusable map-analysis dataset instead of rendering a report-specific one-off view model
- **Decision**: Compute a structured map-analysis dataset that contains railroad summary rows, city access percentages, region probabilities, and aggregate trip metrics, and use that same dataset for both the Information tab and recommendation logic.
- **Rationale**: The user explicitly wants the analysis to be useful both for the player and for application recommendations. A shared structured dataset prevents report drift and avoids reparsing rendered UI output.
- **Alternatives considered**:
  - Build the report directly in the component and let recommendation logic scrape or recompute it independently (rejected: duplicated logic and higher mismatch risk).
  - Store only rendered text for the report (rejected: not machine-friendly for recommendation logic).

## Decision 10: Use a tabbed purchase experience with explicit Map and Information tabs
- **Decision**: The purchase experience will expose at least two tabs, `Map` and `Information`, with the Map tab handling inline selection through map clicks and taskbar controls and the Information tab showing the analysis report.
- **Rationale**: This directly matches the amended spec and cleanly separates interaction-heavy map behavior from dense reference information.
- **Alternatives considered**:
  - Keep the report inline in the same surface as the purchase options (rejected: creates visual overload).
  - Open the report in a separate page or modal (rejected: breaks purchase-phase context and tab-switch continuity).

## Decision 12: Show railroad impact in a map overlay instead of a modal detail view
- **Decision**: When the player selects an unowned railroad on the map, show a compact overlay info box anchored to the map that contains price, access change, and monopoly change.
- **Rationale**: This satisfies the requirement to see railroad impact without leaving the map or opening a dialog.
- **Alternatives considered**:
  - Put all railroad impact data only in the taskbar (rejected: weaker spatial association with the selected railroad).
  - Use a separate detail modal (rejected: conflicts with the inline purchase UX).

## Decision 11: Compute railroad report metrics from existing map topology and destination probability data
- **Decision**: Derive railroad summary metrics from the loaded map's railroad definitions, route segments, city definitions, destination probabilities, and payout data already present in the map domain.
- **Rationale**: The sample report is a map-derived summary, so the implementation should stay rooted in the authoritative map model rather than a static prebuilt table.
- **Alternatives considered**:
  - Check in a static reference file for each supported map (rejected: brittle and disconnected from loaded map data).
  - Limit analysis to currently owned railroads only (rejected: the report is intended as a full-map reference and recommendation input).

## Decision 13: Match report categories, not sample numeric output
- **Decision**: Treat the provided report values as illustrative sample data and require parity only in the categories of information shown.
- **Rationale**: The user clarified that the sample numbers are mock data. Binding implementation correctness to those exact values would add unnecessary fidelity risk without improving the intended feature behavior.
- **Alternatives considered**:
  - Require exact numeric reproduction of the sample report (rejected: the sample is not a golden output baseline).
