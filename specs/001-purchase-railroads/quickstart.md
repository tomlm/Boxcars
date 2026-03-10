# Quickstart: Purchase Phase Buying and Map Analysis

## Prerequisites
- .NET 8 SDK installed
- Valid local table storage configuration in `src/Boxcars/appsettings.Development.json`
- Existing Boxcars sample users or another local multiplayer setup

## Run
1. Start the app from repository root:
   - `dotnet run --project src/Boxcars/Boxcars.csproj`
2. Open the game board in a browser.
3. Start or join a game and play until an active player reaches the purchase phase.

## Validate Railroad Purchase Flow
1. Reach the purchase phase with an active player who can afford at least one unowned railroad.
2. Confirm the Map tab shows inline purchase controls rather than a modal dialog.
3. Confirm the map switches to railroad-selection mode and zooms out.
4. Confirm the taskbar shows `Options [RR+Engine Upgrade Options] [BUY] [DECLINE]` and the combobox lists the available railroad and engine options sorted by price from highest to lowest.
5. Click a railroad on the map and verify:
   - The railroad is highlighted on the map.
   - A railroad overlay info box shows price, access change, and monopoly change.
   - The combobox selection synchronizes to the same railroad.
6. Change the combobox selection to a different railroad and verify the map highlight synchronizes to that railroad.
7. Confirm the purchase with the BUY button and verify:
   - Cash decreases by the railroad price.
   - Railroad ownership transfers to the active player.
   - The phase advances to fee payment.
   - The map switches to move mode while remaining zoomed out.

## Validate Map Analysis and Information Tab
1. Reach the purchase phase for any active player.
2. Confirm the purchase experience exposes `Map` and `Information` tabs.
3. Open the `Information` tab.
4. Verify the analysis report includes:
   - railroad summary rows with railroad name/code, price, city count, service %, monopoly %, connection count, and income metric
   - city access percentage rows
   - region probability rows
   - average trip length, average payoff, and average payoff-per-dot metrics
5. Return to the `Map` tab and verify the current purchase selection remains intact.

## Validate Recommendation Inputs
1. Reach the purchase phase with at least one affordable railroad.
2. Confirm the Information tab report renders from the current map.
3. Confirm recommendation logic uses the same analysis dataset by verifying recommendation outputs stay in sync with the report's map-derived values after map or affordability changes.

## Validate Engine Upgrade Flow
1. Reach the purchase phase with an active player who can afford an eligible engine upgrade.
2. Confirm the taskbar combobox offers:
   - Express at $4000 when upgrading from Freight.
   - Superchief at the configured game-settings price when eligible.
3. Select an engine upgrade and verify the taskbar reflects the price and resulting engine level.
4. Confirm the upgrade with the BUY button and verify:
   - Cash decreases by the correct amount.
   - The player's engine updates.
   - No railroad ownership changes occur.
   - The phase advances to fee payment.

## Validate Decline Flow
1. Reach the purchase phase with at least one available purchase option.
2. Confirm the taskbar shows a DECLINE button.
3. Activate DECLINE.
4. Verify no cash, ownership, or engine changes are applied.
5. Verify purchase mode exits for that turn and the phase advances to fee payment.

## Validate Skip Flows
1. Reach the purchase phase with no affordable railroad or engine option while unowned railroads or upgrade paths still exist.
2. Verify no purchase selection is activated.
3. Verify the notification `{Player} does not have enough money to purchase anything.` appears.
4. Verify the map does not enter railroad-selection mode, remains zoomed out, and switches to move mode.
5. Verify the phase advances directly to fee payment.

## Validate No-Opportunity Flow
1. Reach the purchase phase when no unowned railroads remain and no eligible engine upgrade exists.
2. Verify no purchase selection is activated.
3. Verify no affordability notification is shown.
4. Verify the phase advances directly to fee payment.

## Validate Configurable Superchief Price
1. Set `PurchaseRules:SuperchiefPrice` in `src/Boxcars/appsettings.Development.json` to a non-default value.
2. Restart the app.
3. Reach the purchase phase with a player eligible for a Superchief upgrade.
4. Verify the displayed Superchief price matches configuration.
5. Confirm the server rejects mismatched purchase amounts and accepts the configured amount.

## Regression Checks
- Leaving the purchase phase without activating BUY still advances to fee payment without mutating cash, ownership, or engine state.
- Existing railroad purchase tests still pass.
- Existing locomotive upgrade tests still pass with updated pricing rules.
- Turn event history still records railroad purchases, engine purchases, and declined purchases correctly.
- Analysis report generation remains stable for the standard U21 map.
- Switching between `Map` and `Information` tabs does not clear the active purchase selection.
