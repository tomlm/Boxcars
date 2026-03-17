# Quickstart: AI-Controlled Player Turns

## Prerequisites

1. Configure Azure Table storage as usual for the Boxcars app.
2. Add an OpenAI API key to the web app configuration under `OpenAIKey`.
3. Optionally set `OpenAIModel` to override the default `gpt-4o-mini` model.
4. Stop any running `Boxcars.exe` process before full build validation so the output binary is not locked.

## End-to-End Validation Flow

### 1. Create a reusable bot definition

1. Sign in to the app.
2. Open the dashboard page.
3. Use the BOT management UX to create a bot with a name and strategy prompt.
4. Edit the bot and confirm the change is persisted and visible to another signed-in user.

### 2. Validate a dedicated bot seat

1. Start a game with at least one dedicated bot player seat.
2. Advance play until the bot seat becomes active.
3. Verify the server resolves eligible AI-backed phases automatically with no human clicking `TAKE CONTROL`.
4. Verify movement still uses the existing suggested-route behavior.

### 3. Validate ghost mode for a disconnected human seat

1. Start a multiplayer game with at least two human players.
2. Cause one player's seat to disconnect.
3. Enable ghost mode or assign a bot for that disconnected human seat through the in-game management UI.
4. Advance to an eligible AI-backed phase and verify the server resolves the phase automatically.

### 4. Validate multiplayer visibility and durability

1. Observe the game from another connected client and confirm AI actions arrive through normal real-time updates.
2. Reload the game and verify previously committed AI actions restore exactly.
3. Edit the assigned bot definition on the dashboard and confirm the next AI-backed phase uses the updated strategy.

### 5. Validate stop conditions

1. Reconnect the ghosted human player and verify ghost AI stops acting for that seat immediately.
2. Disable ghost mode or release delegated control and verify no further automated actions occur for that human seat.
3. Delete a referenced bot definition and verify the assignment disables safely until a valid bot is selected again.

## Failure-Path Validation

1. Remove or invalidate `OpenAIKey` and verify eligible phases fall back to legal non-blocking outcomes.
2. Force an invalid or stale AI choice and verify the server re-evaluates against current legal options before commit.
3. Simulate concurrent edits to the same bot definition and verify the dashboard surfaces a conflict instead of silently overwriting changes.