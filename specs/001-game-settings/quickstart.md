# Quickstart: Game Creation Settings

## Goal

Verify that new games persist immutable settings, gameplay uses those settings, and legacy games without explicit settings still run with defaults.

## Setup

1. Start the Boxcars app with the normal local development workflow.
2. Ensure at least two player profiles exist so a new game can be created.
3. Use the Create Game page at `/games/new`.

## Scenario 1: Create a game with defaults

1. Open the create-game flow.
2. Confirm the settings step shows these defaults:
   - `StartingCash = 20000`
   - `AnnouncingCash = 250000`
   - `WinningCash = 300000`
   - `RoverCash = 50000`
   - `PublicFee = 1000`
   - `PrivateFee = 1000`
   - `UnfriendlyFee1 = 5000`
   - `UnfriendlyFee2 = 10000`
   - `HomeSwapping = true`
   - `HomeCityChoice = true`
   - `KeepCashSecret = true`
   - `StartEngine = Freight`
   - `SuperchiefPrice = 40000`
   - `ExpressPrice = 4000`
3. Create the game.
4. Confirm the persisted game shows the same settings and players begin with `Freight` and `20000` cash.

## Scenario 2: Create a game with custom settings

1. Create a new game with these overrides:
   - `StartingCash = 35000`
   - `AnnouncingCash = 150000`
   - `WinningCash = 325000`
   - `PublicFee = 1500`
   - `PrivateFee = 500`
   - `UnfriendlyFee1 = 6000`
   - `UnfriendlyFee2 = 12000`
   - `KeepCashSecret = false`
   - `StartEngine = Express`
   - `ExpressPrice = 6000`
   - `SuperchiefPrice = 45000`
2. Confirm the created game starts every player with `35000` cash and `Express`.
3. During purchase opportunities, confirm `Express` upgrades are unavailable because players already start with it and `Superchief` costs `45000`.
4. During route-fee resolution, confirm public/private/unfriendly fees use the configured custom values.
5. Confirm opponents always see exact cash because `KeepCashSecret = false`.

## Scenario 3: Cash secrecy threshold behavior

1. Create a game with `KeepCashSecret = true` and `AnnouncingCash = 100000`.
2. Observe an opponent below `100000` cash and confirm the display is concealed.
3. Increase that player's cash to at least `100000` and confirm the exact cash becomes visible to opponents.
4. Reduce the same player's cash below `100000` and confirm the display becomes concealed again.

## Scenario 4: Post-start immutability

1. Start a game with any non-default setting values.
2. Attempt to revisit or modify settings after gameplay has started.
3. Confirm the UI shows the saved settings as read-only and no persisted values change.

## Scenario 5: Legacy compatibility

1. Load an older game row that does not contain the new game-setting columns or has them unset.
2. Confirm the game still opens successfully.
3. Verify gameplay behaves as if the documented default settings were in effect.

## Regression Focus

Run targeted automated coverage for:

1. Create-game request validation and persistence of settings.
2. Engine rule checks for start cash, fee calculation, declaration/win checks, and upgrade pricing.
3. Cash-display and advisory projections using resolved per-game settings.
4. Legacy fallback when persisted game-setting columns are absent or null.
