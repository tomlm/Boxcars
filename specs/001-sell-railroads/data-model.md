# Data Model: Forced Railroad Sales and Auctions

## Entity: ForcedSalePhaseModel
- **Purpose**: UI-facing state for an active player who must raise cash during fee resolution.
- **Fields**:
  - `PlayerIndex` (int, required)
  - `PlayerName` (string, required)
  - `AmountOwed` (int, required)
  - `CashOnHand` (int, required)
  - `FeeShortfall` (int, required)
  - `SaleCandidates` (collection of `SaleCandidate`)
  - `SelectedRailroadIndex` (int?, optional)
  - `CurrentNetwork` (`NetworkCoverageSnapshot`, optional)
  - `ProjectedNetworkAfterSale` (`NetworkCoverageSnapshot`, optional)
  - `NetworkTab` (`NetworkTabModel`, required)
  - `AuctionState` (`AuctionStateModel`, optional)
  - `CanSellToBank` (bool, required)
  - `CanStartAuction` (bool, required)
  - `CanResolveFees` (bool, required)
- **Validation**:
  - Present only when the active player owes more than current cash and still has at least one railroad to liquidate, or when an auction is actively in progress.
  - `CanResolveFees` becomes true only when `CashOnHand >= AmountOwed` and no auction is active.

## Entity: SaleCandidate
- **Purpose**: One owned railroad eligible for forced sale.
- **Fields**:
  - `RailroadIndex` (int, required)
  - `RailroadName` (string, required)
  - `ShortName` (string, optional)
  - `OriginalPurchasePrice` (int, required)
  - `BankSalePrice` (int, required)
  - `IsSelected` (bool, required)
  - `SaleImpact` (`SaleImpactSnapshot`, optional)
- **Validation**:
  - The railroad must be owned by the active player.
  - `BankSalePrice` must equal exactly half of the authoritative original purchase price.

## Entity: SaleImpactSnapshot
- **Purpose**: Advisory before-and-after comparison for selling a selected railroad.
- **Fields**:
  - `RailroadIndex` (int, required)
  - `AccessPercentBefore` (decimal, required)
  - `AccessPercentAfter` (decimal, required)
  - `AccessPercentDelta` (decimal, required)
  - `MonopolyPercentBefore` (decimal, required)
  - `MonopolyPercentAfter` (decimal, required)
  - `MonopolyPercentDelta` (decimal, required)
- **Validation**:
  - Values are derived from shared network coverage services using current ownership and projected ownership removal.

## Entity: NetworkTabModel
- **Purpose**: Always-available player network reference shown from the authenticated player's perspective.
- **Fields**:
  - `PlayerName` (string, required)
  - `RailroadSummaries` (collection of `NetworkRailroadSummary`)
  - `SelectedRailroadImpact` (`SaleImpactSnapshot`, optional)
- **Validation**:
  - `RailroadSummaries` show current ownership/network values even when the forced-sale flow is not active.
  - `SelectedRailroadImpact` is populated only when a sale candidate is selected.

## Entity: NetworkRailroadSummary
- **Purpose**: One owned railroad's contribution in the player's network reference view.
- **Fields**:
  - `RailroadIndex` (int, required)
  - `RailroadName` (string, required)
  - `AccessPercentWithCurrentOwnership` (decimal, required)
  - `MonopolyPercentWithCurrentOwnership` (decimal, required)
  - `AccessPercentIfSold` (decimal, optional)
  - `MonopolyPercentIfSold` (decimal, optional)

## Entity: AuctionStateModel
- **Purpose**: Persisted shared state for an in-progress railroad auction.
- **Fields**:
  - `RailroadIndex` (int, required)
  - `RailroadName` (string, required)
  - `SellerPlayerIndex` (int, required)
  - `SellerPlayerName` (string, required)
  - `StartingPrice` (int, required)
  - `CurrentBid` (int, required)
  - `LastBidderPlayerIndex` (int?, optional)
  - `CurrentBidderPlayerIndex` (int?, optional)
  - `RoundNumber` (int, required)
  - `ConsecutiveNoBidTurnCount` (int, required)
  - `Participants` (collection of `AuctionParticipantState`)
  - `Status` (enum, required: `Open`, `Awarded`, `BankFallback`)
- **Validation**:
  - `StartingPrice` equals half of the railroad's original purchase price.
  - `CurrentBid` is `0` until the first valid bid, then remains greater than or equal to `StartingPrice`.
  - Only one participant may be marked as the current bidder turn at a time.

## Entity: AuctionParticipantState
- **Purpose**: Per-player auction eligibility and current standing.
- **Fields**:
  - `PlayerIndex` (int, required)
  - `PlayerName` (string, required)
  - `CashOnHand` (int, required)
  - `IsEligible` (bool, required)
  - `HasDroppedOut` (bool, required)
  - `HasPassedThisRound` (bool, required)
  - `LastAction` (enum, required: `None`, `Bid`, `Pass`, `DropOut`, `AutoDropOut`)
- **Validation**:
  - Seller and eliminated players are not eligible participants.
  - A participant with `HasDroppedOut = true` cannot receive future turns in the same auction.

## Entity: AuctionDecision
- **Purpose**: One persisted player action taken during an auction turn.
- **Fields**:
  - `RailroadIndex` (int, required)
  - `PlayerIndex` (int, required)
  - `DecisionKind` (enum, required: `Bid`, `Pass`, `DropOut`)
  - `BidAmount` (int?, optional)
  - `OccurredAtUtc` (datetime, required)
- **Validation**:
  - `BidAmount` is required only for `Bid`.
  - A bid above `CashOnHand` is normalized into an automatic drop-out result before auction state is advanced.

## Entity: FeeResolutionStatus
- **Purpose**: The authoritative state of fee payment while forced sale may still be required.
- **Fields**:
  - `AmountOwed` (int, required)
  - `CashBeforeFees` (int, required)
  - `CashAfterLastSale` (int, required)
  - `CanPayNow` (bool, required)
  - `SalesCompletedCount` (int, required)
  - `EliminationTriggered` (bool, required)

## Entity: PlayerEliminationState
- **Purpose**: The persisted spectator-only outcome for a player who cannot cover required fees.
- **Fields**:
  - `PlayerIndex` (int, required)
  - `PlayerName` (string, required)
  - `Reason` (string, required)
  - `CanObserve` (bool, required)
  - `CanAct` (bool, required)
- **Validation**:
  - `CanObserve` must remain true.
  - `CanAct` must remain false for all later game actions.

## Relationships
- One `ForcedSalePhaseModel` has many `SaleCandidate` items.
- One selected `SaleCandidate` may produce one `SaleImpactSnapshot`.
- One `ForcedSalePhaseModel` has one `NetworkTabModel`.
- One `ForcedSalePhaseModel` has at most one active `AuctionStateModel`.
- One `AuctionStateModel` has many `AuctionParticipantState` items.
- One `AuctionStateModel` accumulates many `AuctionDecision` actions over time.
- One `FeeResolutionStatus` may result in one `PlayerEliminationState`.

## State Transitions
- `UseFeesReady` → `FeesPaidWithoutLiquidation`
- `UseFeesReady` → `ForcedSaleRequired`
- `ForcedSaleRequired` → `RailroadSelectedForSale`
- `RailroadSelectedForSale` → `BankSaleCommitted`
- `RailroadSelectedForSale` → `AuctionOpened`
- `AuctionOpened` → `AwaitingParticipantDecision`
- `AwaitingParticipantDecision` → `BidRecorded`
- `AwaitingParticipantDecision` → `ParticipantPassed`
- `AwaitingParticipantDecision` → `ParticipantDroppedOut`
- `BidRecorded` → `AwaitingParticipantDecision`
- `ParticipantPassed` → `AwaitingParticipantDecision`
- `ParticipantDroppedOut` → `AwaitingParticipantDecision`
- `AwaitingParticipantDecision` → `AuctionAwardedToBidder`
- `AwaitingParticipantDecision` → `AuctionFellBackToBankSale`
- `BankSaleCommitted` → `FeesReevaluated`
- `AuctionAwardedToBidder` → `FeesReevaluated`
- `AuctionFellBackToBankSale` → `FeesReevaluated`
- `FeesReevaluated` → `ForcedSaleRequired`
- `FeesReevaluated` → `FeesPaidAfterLiquidation`
- `FeesReevaluated` → `PlayerEliminated`
