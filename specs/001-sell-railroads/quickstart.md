# Quickstart: Forced Railroad Sales and Auctions

## Prerequisites

- Build the solution.
- Start a multiplayer game with at least two active players.
- Ensure one player reaches fee resolution with insufficient cash and owns at least one railroad.

## Scenario 1: Direct bank sale covers the debt

1. Advance the active player to `UseFees` with `cash < amount owed`.
2. Verify the forced-sale experience opens instead of ending the turn.
3. Select one owned railroad on the map.
4. Confirm the railroad info panel and `Network` tab show the projected access and monopoly loss.
5. Choose `Sell To Bank`.
6. Verify the player receives exactly half the railroad's original purchase price.
7. Verify the railroad becomes unowned and available for future normal purchase.
8. Verify fees are re-evaluated immediately and the turn proceeds only if the debt is now covered.

## Scenario 2: Auction transfers ownership to another player

1. Enter forced sale with at least one other active player able to bid.
2. Select an owned railroad and choose `AUCTION`.
3. Verify the starting price equals half the original purchase price.
4. Have one bidder place a valid bid and later players either pass or drop out.
5. Complete a full round with no new bid.
6. Verify the last bidder wins, cash transfers from buyer to seller, and railroad ownership moves to the winner.
7. Verify the result is persisted and visible in the game timeline/state for all players.

## Scenario 3: Auction falls back to the bank

1. Start an auction for an owned railroad.
2. Have every eligible bidder pass or drop out without placing a valid bid.
3. Verify the railroad resolves as a bank sale at half price.
4. Verify the railroad returns to the unowned pool and fees are re-evaluated immediately.

## Scenario 4: Player is eliminated after exhausting sales

1. Set up a player whose cash plus total sale value of all owned railroads is still below the amount owed.
2. Complete the required bank sales or no-bid auctions until no owned railroads remain.
3. Verify the player is marked out of active play.
4. Verify the eliminated player can still observe the game state but cannot submit gameplay actions or join later auctions.

## Regression Checks

1. Verify purchase-phase railroad buying and engine upgrades still transition into `UseFees` correctly.
2. Verify existing network analysis for purchase remains consistent with sale-impact values for the same ownership graph.
3. Verify reconnecting clients see the current forced-sale or auction state without manual refresh.
