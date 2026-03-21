# Quickstart: AI Advice Chat

## Prerequisites

1. Configure Azure Table storage for the Boxcars app as usual.
2. Add a valid OpenAI API key in app configuration under `OpenAIKey` and confirm a model is configured.
3. Ensure the app can load and join an active game board.

## End-to-End Validation Flow

### 1. Open the advisor from the board

1. Start the app and sign in.
2. Open any active game board.
3. Click the advisor icon in the lower-right corner.
4. Verify a sidebar chat panel opens without navigating away from the board.
5. Verify the first assistant message is exactly "How can I help?"

### 2. Ask board-aware questions

1. With the advisor open, ask a question about the current board, such as current cash pressure, destination status, or whether to buy a railroad.
2. Verify the answer references the current player context and current board situation.
3. Ask a follow-up question about a different strategic concern and verify the conversation remains visible in the sidebar.

### 3. Verify latest-state refresh behavior

1. Leave the advisor open.
2. Advance the game state through a move, purchase, payout, fee event, or turn transition.
3. Ask another question.
4. Verify the answer reflects the updated game state rather than the earlier board condition.

### 4. Verify controlled-seat strategy behavior

1. Use a board session where the current user is acting for a delegated or otherwise controlled seat.
2. Open the advisor and ask for strategic advice.
3. Verify the response is framed around the controlled player's resources and position, not a different seat.

### 5. Verify graceful failure handling

1. Remove or invalidate the OpenAI API key, or simulate provider unavailability.
2. Open the advisor and submit a question.
3. Verify the board remains usable, the sidebar shows a clear retryable failure message, and no gameplay state changes occur.

## Validation Notes

1. Advice must remain advisory and must never claim to have committed an action.
2. The board should remain interactive while the advisor is open.
3. Mobile validation should confirm the sidebar remains readable and dismissible without obscuring all core gameplay controls.