# Feature Specification: Forced Railroad Sales and Auctions

**Feature Branch**: `001-sell-railroads`  
**Created**: 2026-03-12  
**Status**: Draft  
**Input**: User description: "Selling railroads When the user needs to pay their fees, if they don't have enough funds they must sell a RR. There are 2 options for selling a RR. You can sell it to the bank for 1/2 of the purchase price. That RR is now available for other people to buy during a normal purchse phase. You can auction it. To auction a RR the player whose turn it is has ability to select the railroad to auction. The map should be in RR Selection mode, only you can only select the RR that the player owns. When a RR is selected the RR Info panel should show the impact of selling the RR (change in Access and Monopoly) just like the Purchase information, only it should show the impact of selling it. There should be another tab called Network added and available at all times. This page shows the stats for each RR owned by the authenticed player, and the impact of selling the RR would have on the network. When a RR is selected there is a button AUCTION. Auction starts a Multiplayer auction with the RR that was selected when AUCTION is pressed as the RR being auctioned. The starting price is 1/2 the original price. Each player in order has option to bid on the RR, pass or drop out. Bid - they enter in an $ amount and that's an event that gets persisted and propagated, moving the bid to the next player. Pass - they pass meaning they declined to bid. They are still in the auction but if only 1 person makes a bid they win. Drop out - They no longer want to participate in the auction and so no longer have an auction turn. If the bid is more than the amount of cash they have on hand then they are automatically dropped out. When it makes 1 round with noone bidding the last bidder buys the RR, The amount bid is transferred to the seller, and the buyer gets the ownership of the RR. This is an event which is recorded and propagated. If noone bids, then the RR is sold to the bank for 1/2 price player gets the money added to their cash. The player who owes fees has to keep selling RR until they have enough money to pay their fees and end their turn. If they run out of money and RR they are out of the game permantly, and can only watch the game play."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Raise cash during fee payment (Priority: P1)

As the active player, I want to sell one of my owned railroads when I cannot pay required fees so I can keep playing if I can still raise enough cash.

**Why this priority**: Forced liquidation is the core gameplay need. Without it, the fee-payment step cannot resolve correctly when a player is short on cash.

**Independent Test**: Can be fully tested by starting fee payment with an active player whose cash is below the amount owed, selling one owned railroad to the bank, and verifying that the player's cash increases correctly, the railroad becomes available again, and the fee-payment step continues until the debt is covered.

**Acceptance Scenarios**:

1. **Given** the active player owes fees and has less cash than the amount owed but still owns at least one railroad, **When** the forced-sale flow begins, **Then** the player is required to choose one of their owned railroads before fee payment can complete.
2. **Given** the active player selects one of their owned railroads for a bank sale, **When** the sale is confirmed, **Then** the player receives exactly half of that railroad's purchase price, ownership of that railroad is removed from the player, and that railroad becomes available for future normal purchase phases.
3. **Given** the active player still has less cash than the amount owed after a sale completes, **When** fee payment is re-evaluated, **Then** the player must continue selling owned railroads until the debt can be paid or the player has no railroad left to sell.
4. **Given** the active player has raised enough cash to cover the amount owed, **When** fee payment resolves, **Then** the required fees are paid and the turn may proceed to its normal end-of-turn flow.

---

### User Story 2 - Evaluate which railroad to sell (Priority: P2)

As the active player, I want the map and railroad information panels to show the impact of selling each railroad so I can choose the least damaging option for my network.

**Why this priority**: Selling the wrong railroad can materially weaken a player's position. The feature needs clear decision support, not just a liquidation button.

**Independent Test**: Can be fully tested by entering the forced-sale flow, selecting different owned railroads from the map, and verifying that the selection rules, railroad information panel, and always-available Network tab all update to show the effect of selling the currently selected railroad.

**Acceptance Scenarios**:

1. **Given** the active player is required to sell a railroad, **When** the map enters railroad-selection mode, **Then** only railroads currently owned by that player can be selected for sale.
2. **Given** one of the active player's owned railroads is selected, **When** the railroad information panel is shown, **Then** it displays the projected change in network access and monopoly caused by selling that railroad.
3. **Given** the player opens the `Network` tab, **When** no railroad is selected, **Then** the tab shows the authenticated player's current railroad-by-railroad network statistics.
4. **Given** the player opens the `Network` tab while a railroad is selected for sale, **When** the projected sale is evaluated, **Then** the tab shows the impact that selling that selected railroad would have on the player's network.

---

### User Story 3 - Auction a railroad to other players (Priority: P3)

As the active player, I want to auction one of my railroads to the other players instead of taking the bank value so I can try to raise more cash.

**Why this priority**: Auctioning is a distinct strategic option with multiplayer consequences and must follow a clear, fair turn order.

**Independent Test**: Can be fully tested by starting a forced sale, selecting an owned railroad, launching an auction, having multiple players bid, pass, or drop out, and verifying that the winning bid or fallback bank sale is applied correctly and visible to all players.

**Acceptance Scenarios**:

1. **Given** the active player has selected one of their owned railroads for sale, **When** they choose `AUCTION`, **Then** a multiplayer auction begins for that railroad with a starting price equal to half of the railroad's original purchase price.
2. **Given** an auction is in progress, **When** a player submits a valid bid that they can afford, **Then** the bid becomes the current leading bid and the auction advances to the next eligible player in turn order.
3. **Given** an auction is in progress, **When** a player chooses to pass, **Then** that player remains eligible to act in later auction rounds unless they later drop out.
4. **Given** an auction is in progress, **When** a player chooses to drop out, **Then** that player is removed from further turns in that auction.
5. **Given** a full round completes with no new bid after at least one valid bid has been made, **When** the auction closes, **Then** the last bidder buys the railroad, the winning amount is transferred to the seller, and ownership transfers to the winning bidder.
6. **Given** no player makes any valid bid during the auction, **When** the auction closes, **Then** the railroad is sold to the bank for half of its original purchase price and that railroad becomes available again for future normal purchase phases.

---

### User Story 4 - Leave the game when unable to cover debt (Priority: P4)

As a player who cannot raise enough cash even after selling all railroads, I want the game to eliminate me cleanly so the rest of the table can continue while I remain a spectator.

**Why this priority**: Insolvency is a terminal game state and needs a defined outcome to prevent the game from stalling.

**Independent Test**: Can be fully tested by starting fee payment with a player whose cash plus all possible railroad sale value is still below the amount owed, completing all required sales, and confirming that the player is marked out of the game and can no longer take gameplay actions.

**Acceptance Scenarios**:

1. **Given** the active player owes more than they can pay even after selling every owned railroad, **When** the final sale resolves and fee payment is re-evaluated, **Then** the player is removed from active play.
2. **Given** a player has been removed from active play for insolvency, **When** the game continues, **Then** that player can observe the game state but cannot take further gameplay actions.

### Edge Cases

- If the active player owes fees but already has enough cash, the forced-sale flow must not open.
- If the active player owns exactly one railroad, the sale flow must still allow that railroad to be selected and resolved correctly.
- If selling a railroad causes the player to lose access or monopoly benefits, the projected loss must be shown before the player confirms the sale.
- If a player attempts to bid more cash than they currently hold, that player must be removed from the current auction instead of remaining eligible with an invalid bid.
- If every non-seller player passes through an auction without ever placing a bid, the auction must fall back to a bank sale at half price.
- If only one valid bidder ever places a bid and every later action in the round is pass or drop out, that bidder must win when the round closes with no new bids.
- If the active player raises some cash but still cannot cover the full debt, the system must return immediately to the next required railroad sale instead of allowing the turn to end early.
- If the active player has no cash and no railroad to sell while fees are still owed, the player must be eliminated without entering an unusable sale flow.
- If a player has been eliminated from the game, that player must not participate in auctions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST interrupt normal turn completion with a forced-sale flow whenever the active player owes fees and has less cash than the amount required.
- **FR-002**: The forced-sale flow MUST allow the active player to choose one owned railroad at a time to sell.
- **FR-002A**: During forced sale selection, the map MUST enter railroad-selection mode.
- **FR-002B**: During forced sale selection, the map MUST allow selection only of railroads currently owned by the active player.
- **FR-003**: For any railroad selected for sale, the system MUST show the projected change in the active player's network access and monopoly if that railroad is sold.
- **FR-003A**: The railroad information panel MUST present sale impact information in the same decision step as the sale selection.
- **FR-003B**: The system MUST provide a `Network` tab that is available at all times, including outside the forced-sale flow.
- **FR-003C**: The `Network` tab MUST show the authenticated player's railroad-by-railroad network statistics.
- **FR-003D**: When a railroad is selected for sale, the `Network` tab MUST show the projected network impact of selling that railroad.
- **FR-004**: The forced-sale flow MUST offer exactly two sale paths for the selected railroad: bank sale and auction.
- **FR-005**: When the active player chooses a bank sale, the system MUST credit the seller with exactly half of the railroad's original purchase price.
- **FR-005A**: After a bank sale completes, the sold railroad MUST no longer be owned by that player and MUST become available again during later normal railroad purchase opportunities.
- **FR-006**: When the active player chooses `AUCTION`, the system MUST start a multiplayer auction for the currently selected railroad.
- **FR-006A**: The auction's starting price MUST be exactly half of the railroad's original purchase price.
- **FR-006B**: Only players who remain active in the game other than the seller MAY participate in the auction.
- **FR-007**: During an auction, each eligible participant MUST receive auction turns in player order.
- **FR-007A**: On that player's auction turn, the system MUST allow exactly one of these actions: place a bid, pass, or drop out.
- **FR-007B**: A bid MUST identify a specific dollar amount greater than or equal to the current required bid.
- **FR-007C**: A pass MUST keep that player eligible for later auction turns in the same auction.
- **FR-007D**: A drop out action MUST remove that player from further turns in the same auction.
- **FR-008**: If a player attempts to place a bid that exceeds the cash they currently hold, the system MUST automatically treat that player as dropped out of the auction.
- **FR-009**: Each bid, pass, drop out, auction result, and ownership transfer MUST be recorded as a game event and propagated to all connected players.
- **FR-010**: If a complete round of auction turns finishes without any new bid after at least one valid bid has been made, the auction MUST close and award the railroad to the last bidder.
- **FR-010A**: When an auction closes with a winning bidder, the winning amount MUST be transferred to the seller and ownership of the railroad MUST transfer to the winning bidder.
- **FR-011**: If an auction closes without any valid bid being made, the system MUST complete the sale as a bank sale for half of the railroad's original purchase price.
- **FR-012**: After each railroad sale completes, the system MUST immediately re-evaluate whether the active player can now pay the required fees.
- **FR-012A**: If the active player still cannot pay the required fees and still owns one or more railroads, the system MUST require another railroad sale before the turn can continue.
- **FR-012B**: If the active player can pay the required fees after the sale, the system MUST resolve the fee payment before the turn may end.
- **FR-013**: If the active player cannot pay the required fees and has no remaining railroad to sell, the system MUST remove that player from active play.
- **FR-013A**: A player removed from active play for insolvency MUST remain able to observe the game.
- **FR-013B**: A player removed from active play for insolvency MUST NOT be allowed to take further gameplay actions.
- **FR-014**: The system MUST preserve the authoritative turn state across forced sales and auctions so every connected player sees the same seller, selected railroad, current bid, current bidder, and sale result.

### Key Entities *(include if feature involves data)*

- **Fee Shortfall**: The difference between the amount the active player owes and the cash they currently hold when fee payment begins.
- **Sale Candidate**: One railroad currently owned by the active player that can be selected for bank sale or auction during forced liquidation.
- **Sale Impact Snapshot**: The before-and-after network summary for a selected railroad sale, including projected changes to access and monopoly.
- **Network Railroad Summary**: A per-railroad view of the authenticated player's network position used in the always-available `Network` tab.
- **Auction Session**: The temporary shared state for a railroad auction, including the railroad being sold, seller, starting price, active participants, current bidder turn, leading bid, and last bidder.
- **Auction Decision**: A participant's auction-turn action, consisting of a bid amount, pass, or drop out.
- **Player Elimination State**: The status applied when a player cannot satisfy required fees and has exhausted all sale options, preventing further gameplay actions while preserving spectator visibility.

## Assumptions

- Forced railroad sales occur only while resolving required fee payment during the active player's turn.
- The same authoritative network measures already used for railroad purchasing analysis define access and monopoly changes for sale impact.
- A railroad sold to the bank returns to the pool of railroads that may later be bought through the game's normal railroad purchase flow.
- The seller does not participate as a bidder in the auction for their own railroad.
- Players who have already been eliminated from the game may observe auctions but may not bid, pass, or drop out because they are no longer active participants.
- The authenticated player's `Network` tab may be opened outside forced-sale situations, but sale-impact projections appear only when a railroad is currently selected for sale.
- The active player may need to repeat the forced-sale flow multiple times within one fee-payment step until the debt is covered or the player is eliminated.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In rules-based playtesting, 100% of fee-payment situations where a player has insufficient cash but owns at least one railroad enter the forced-sale flow before the turn can end.
- **SC-002**: In validation testing, 100% of completed bank sales credit exactly half of the sold railroad's original purchase price and return that railroad to future purchase availability.
- **SC-003**: In multiplayer auction testing, 100% of bid, pass, drop out, and auction-result actions are visible to all connected players in the same order they were taken.
- **SC-004**: In auction outcome testing, 100% of auctions with at least one valid bid end with ownership transferring to the last bidder after a full round with no new bids, and 100% of auctions with no valid bids fall back to a bank sale at half price.
- **SC-005**: In usability testing, players can identify the projected access and monopoly loss for a selected railroad before confirming a sale without leaving the sale-selection experience.
- **SC-006**: In insolvency testing, 100% of players who still cannot cover required fees after exhausting all railroad sales are removed from active play and prevented from taking further gameplay actions while remaining able to observe the game.
