# Quickstart: AI-Controlled Player Turns

## Prerequisites

1. Configure Azure Table storage as usual for the Boxcars app.
2. Add an OpenAI API key to the web app configuration under `OpenAIKey`.
3. Optionally set `OpenAIModel` to override the default `gpt-4o-mini` model.
4. Build the solution and confirm the dashboard and gameplay flows run normally before enabling bot behavior.

## End-to-End Validation Flow

### 1. Create a reusable bot definition

1. Sign in to the app.
2. Open the dashboard page.
3. Use the BOT management UX to create a bot with a name and strategy prompt.
4. Edit the bot and confirm the change is persisted and visible to another signed-in user.

### 2. Assign a bot to a disconnected controlled seat

1. Start a multiplayer game with at least two human players.
2. Cause one player's seat to enter the disconnected state.
3. From another connected player, use `TAKE CONTROL` for the disconnected seat.
4. Open the controlled player's card and verify a settings icon appears beside `RELEASE`.
5. Open the settings action and assign the bot created on the dashboard.

### 3. Validate bot-driven turn progression

1. Advance the disconnected player's turn into destination-region selection and verify the phase resolves automatically.
2. Advance into a purchase opportunity and verify the server commits either a legal purchase or a legal no-purchase outcome.
3. Advance into an auction and verify the server commits a legal bid or pass/no-bid outcome.
4. Advance into movement and verify the system uses the existing suggested-path behavior rather than requesting an AI move.
5. Force a sell scenario and verify the chosen railroad sale is deterministic and low-impact according to access/monopoly evaluation.

### 4. Validate multiplayer visibility and durability

1. Observe the game from another connected client and confirm bot actions arrive through normal real-time updates.
2. Reload the game and verify previously committed bot actions restore exactly.
3. Edit the assigned bot definition on the dashboard and confirm the next AI-backed phase uses the updated strategy.

### 5. Validate stop conditions

1. Click `RELEASE` from the controlling player and verify the bot stops acting immediately.
2. Reassign the bot and then reconnect the original disconnected player.
3. Verify the bot assignment is cleared and no further automated actions occur for that seat.

## Failure-Path Validation

1. Remove or invalidate `OpenAIKey` and verify eligible phases fall back to legal non-blocking outcomes.
2. Delete a bot definition that is currently assigned and verify the assignment is disabled safely.
3. Simulate concurrent edits to the same bot definition and verify the dashboard surfaces a conflict instead of silently overwriting changes.