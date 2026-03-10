# Contract: Purchase Phase UI, Inline Selection, Map Analysis, and Action Flow

## Purpose
Defines the user-facing and server-action contract for the purchase phase on the game board.

## Surface
- **Consumer**: Active player using the game board purchase UI
- **Provider**: `GameBoard.razor`, taskbar purchase controls, railroad overlay UI, `GameBoardStateMapper`, `GameEngineService`, and `Boxcars.Engine`

## Input Contract

### Purchase Phase State
- **Source**: Current `RailBaronGameState` plus mapped purchase-phase view data
- **Required fields**:
  - active player index/name
  - cash after payout
  - destination city name
  - available railroad purchase options
  - available engine upgrade options
  - taskbar combobox entries sorted high to low by price
  - selected tab (`Map` or `Information`)
  - map analysis report payload
  - current coverage snapshot
  - projected coverage snapshot for selected railroad
  - railroad overlay info for the current railroad selection

### Purchase Selection
- **Railroad selection**:
  - `railroadIndex`
  - derived `amountPaid`
  - synchronized taskbar option key
- **Engine upgrade selection**:
  - `engineType`
  - derived `amountPaid`

### Confirm Actions
- **Railroad commit**: `PurchaseRailroadAction`
  - `PlayerId`
  - `ActorUserId`
  - `PlayerIndex`
  - `RailroadIndex`
  - `AmountPaid`
- **Engine commit**: `BuyEngineAction`
  - `PlayerId`
  - `ActorUserId`
  - `PlayerIndex`
  - `EngineType`
  - `AmountPaid`
- **Decline**: `DeclinePurchaseAction`
  - `PlayerId`
  - `ActorUserId`
  - `PlayerIndex`

## Output Contract

### Purchase Controls Active State
- `status`: `open`
- `tabs`: `Map`, `Information`
- `activeTab`: one of `Map` or `Information`
- `selectionMode`: `Railroad` when the `Map` tab is active for railroad selection
- `viewportModeAfterOpen`: `ZoomedOut`
- `taskbar`: `Options [RR+Engine Upgrade Options] [BUY] [DECLINE]`
- `taskbarOptions`: sorted descending by price and containing the available railroad and engine purchase options
- `engineOptions`: eligible forward upgrades only
- `railroadOverlayInfo`: present when a railroad is selected on the map
- `mapAnalysisReport`: structured railroad/city/region/trip summary data available to the `Information` tab

### Purchase Phase Skipped State
- `status`: `skipped`
- `selectionMode`: unchanged / does not enter railroad-selection mode
- `viewportModeAfterSkip`: `Move`
- `zoomStateAfterSkip`: `ZoomedOut`
- `notification`: either affordability message or absent when no eligible purchase option exists

### Successful Railroad Purchase
- `status`: `committed`
- `purchaseKind`: `railroad`
- `cashDelta`: negative railroad price
- `ownershipChange`: selected railroad now owned by active player
- `nextPhase`: `UseFees`

### Successful Engine Upgrade
- `status`: `committed`
- `purchaseKind`: `engine`
- `cashDelta`: negative engine price
- `engineChange`: player engine updated to selected target level
- `nextPhase`: `UseFees`

### Declined or Failed Purchase
- `status`: `declined` or `rejected`
- `cashDelta`: `0`
- `ownershipChange`: none
- `engineChange`: none
- `nextPhase`: `UseFees` for decline; unchanged for rejected confirmation until the player chooses again or the phase otherwise resolves

## Behavioral Guarantees
- Exactly one purchase action may be committed during a purchase phase.
- Client UI may preview railroad effects, but only the server-authoritative action result changes cash, ownership, or engine state.
- Railroad selections update map highlighting, overlay info, projected coverage statistics, and synchronized taskbar selection.
- Engine upgrade selections do not require map highlighting.
- Activating DECLINE exits purchase mode for that turn without applying any asset purchase.
- The `Information` tab report is generated from the same map-analysis dataset that recommendation logic consumes.
- Switching between the `Map` and `Information` tabs preserves the current purchase selection.
- When the purchase phase ends or is skipped, the map ends in move mode while remaining zoomed out.

## Error Contract
- `insufficient-funds`: selected option cannot be purchased at current cash level
- `railroad-unavailable`: railroad became owned/unavailable before confirmation
- `invalid-upgrade-path`: selected engine upgrade is not valid from the current engine
- `price-mismatch`: submitted amount does not match authoritative configured/game-derived price
- `wrong-phase`: purchase action submitted outside the purchase phase
- `not-controller`: acting user is not authorized to control the active player slot
