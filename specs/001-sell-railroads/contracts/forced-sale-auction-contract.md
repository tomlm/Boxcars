# Contract: Forced Sale, Network View, and Multiplayer Auction Flow

## Input Contract

### Forced Sale State
- **Source**: Current authoritative game snapshot plus mapped board-state data for the authenticated player.
- **Required fields**:
  - active player index/name
  - amount currently owed in fees
  - cash on hand
  - owned railroad sale candidates
  - bank sale value for each candidate
  - current network coverage snapshot
  - projected sale-impact snapshot for the current selection
  - always-available authenticated-player network summaries
  - in-progress auction state when present

### Sale Selection
- **Source**: Map selection or taskbar/list selection during forced liquidation.
- **Required fields**:
  - `railroadIndex`
  - `playerIndex`
  - derived `bankSalePrice`
  - derived sale-impact snapshot

### Player Actions

#### Direct Bank Sale
- `SellRailroadAction`
  - `PlayerId`
  - `ActorUserId`
  - `PlayerIndex`
  - `RailroadIndex`
  - `AmountReceived = 0` to request authoritative half-price bank resolution

#### Start Auction
- `StartAuctionAction`
  - `PlayerId`
  - `ActorUserId`
  - `PlayerIndex`
  - `RailroadIndex`

#### Auction Bid
- `BidAction`
  - `PlayerId`
  - `ActorUserId`
  - `PlayerIndex`
  - `RailroadIndex`
  - `AmountBid`

#### Auction Pass
- `AuctionPassAction`
  - `PlayerId`
  - `ActorUserId`
  - `PlayerIndex`
  - `RailroadIndex`

#### Auction Drop Out
- `AuctionDropOutAction`
  - `PlayerId`
  - `ActorUserId`
  - `PlayerIndex`
  - `RailroadIndex`

## Output Contract

### Forced Sale Active State
- `status`: `forced-sale-required`
- `phase`: `UseFees`
- `selectionMode`: `Railroad`
- `selectableRailroads`: active player's owned railroads only
- `actions`: `Sell To Bank`, `Auction`
- `railroadInfoPanel`: selected railroad sale impact
- `networkTab`: current authenticated-player railroad summaries plus selected sale impact when present

### Auction Open State
- `status`: `auction-open`
- `phase`: `UseFees`
- `auctionRailroad`: selected railroad
- `startingPrice`: half original purchase price
- `currentBid`: current highest bid or `0` before first bid
- `currentBidderTurn`: next eligible player index/name
- `participants`: eligible players with active/dropped-out state
- `allowedActionsForCurrentBidder`: `Bid`, `Pass`, `Drop Out`

### Auction Awarded State
- `status`: `auction-awarded`
- `winnerPlayerIndex`: last bidder
- `winningBid`: final bid
- `cashTransfer`: winning bid added to seller and removed from winner
- `ownershipChange`: auctioned railroad transferred to winner
- `nextStep`: `ReevaluateFees`

### Auction Fallback Bank Sale State
- `status`: `auction-bank-fallback`
- `winnerPlayerIndex`: null
- `amountReceived`: half original purchase price
- `ownershipChange`: railroad returned to bank/unowned pool
- `nextStep`: `ReevaluateFees`

### Insolvency Elimination State
- `status`: `player-eliminated`
- `reason`: insufficient funds after exhausting sale options
- `playerCanObserve`: true
- `playerCanAct`: false

## Behavioral Rules

- Sale-impact values are advisory projections derived from shared network-coverage services and do not authorize the sale by themselves.
- The active player may select only owned railroads while `status = forced-sale-required`.
- Auction turns advance only among eligible non-seller players who have not dropped out and are still active in the game.
- A bid larger than the bidder's current cash is rejected as a valid bid and converted into an automatic drop-out outcome.
- One full participant round without a new bid awards the railroad to the last bidder when a prior bid exists.
- If no valid bid exists when the auction closes, the result is identical to a half-price bank sale.
- Every sale, bid, pass, drop-out, award, fallback, and elimination outcome must be persisted and broadcast through the authoritative game-event pipeline.
