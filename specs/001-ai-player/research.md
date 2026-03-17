# Research: AI-Controlled Player Turns

## Decision 1: Keep OpenAI orchestration server-side and call it through `HttpClient`

- **Decision**: Keep bot decision calls on the server using the existing DI-managed `HttpClient` stack and configuration sourced from `OpenAIKey`, with `gpt-4o-mini` as the default model.
- **Rationale**: This preserves server authority, keeps secrets off clients, and fits the current Boxcars architecture.
- **Alternatives considered**:
  - Official OpenAI .NET SDK: viable later, but unnecessary extra abstraction for this feature.
  - Client-side OpenAI calls: rejected because they would expose credentials and violate server-authoritative multiplayer design.

## Decision 2: Keep the engine player model unchanged; add seat-controller mode in application data

- **Decision**: Do not create separate engine player types for bot players or ghost players. Add an application-layer controller mode that distinguishes `HumanDirect`, `HumanDelegated`, `AiBotSeat`, and `AiGhost`.
- **Rationale**: The engine should continue to model only seats, legal actions, and rule outcomes. Control ownership is orchestration state, not rule state.
- **Alternatives considered**:
  - Add `BotPlayer` and `HumanPlayer` engine types: rejected because it spreads orchestration concerns into the rules layer.
  - Continue inferring control from delegated-controller and bot-assignment combinations: rejected because it conflates dedicated bot seats with ghost mode and keeps the current awkward UI/authorization model.

## Decision 3: Dedicated bot seats must not depend on delegated human control

- **Decision**: Remove the requirement that dedicated bot seats acquire delegated control through a human user in order to act.
- **Rationale**: A dedicated bot seat is already AI-controlled by definition. Requiring a human controller is a modeling workaround that leaks into UI, authorization, and reconnect logic.
- **Alternatives considered**:
  - Keep the current delegated-control bootstrap: rejected because it makes bot seats look like disconnected human seats and forces unnecessary `TAKE CONTROL` semantics.

## Decision 4: Ghost mode remains separate from dedicated bot seats

- **Decision**: Model ghost mode as an AI controller mode for a disconnected human seat, separate from a dedicated bot seat.
- **Rationale**: Ghost mode has human ownership and stop conditions tied to reconnect/release, while dedicated bot seats do not.
- **Alternatives considered**:
  - Treat ghost mode and bot seats identically in persistence: rejected because reconnect/release semantics differ materially.

## Decision 5: AI turns execute only inside the server automatic-turn loop

- **Decision**: Continue using `GameEngineService.AdvanceAutomaticTurnFlowAsync` as the sole execution point for AI-authored turns.
- **Rationale**: This avoids duplicate client execution, preserves one authoritative action stream, and fits the existing event/history pipeline.
- **Alternatives considered**:
  - Let each connected client execute AI for the seat it can see: rejected because it creates race conditions and breaks server authority.
  - Use a separate background worker outside the game service: rejected for now because the existing automatic-turn loop already provides the required sequencing.

## Decision 6: Use a server-owned actor identity for AI-authored actions

- **Decision**: AI-authored actions should be stamped with a server-owned actor identity rather than a delegated human `ControllerUserId`.
- **Rationale**: Dedicated bot seats do not have a human controller, and ghost-mode AI should still be distinguishable from actual human delegated play.
- **Alternatives considered**:
  - Continue stamping AI actions as the delegated human controller: rejected because it misattributes authorship and prevents removal of the delegated-control dependency.

## Decision 7: Authorization must branch on controller mode

- **Decision**: Keep normal human-slot authorization for direct/delegated human play and add a narrow authorization path for server-authored AI actions when the active seat controller mode is AI.
- **Rationale**: This preserves current safety checks while allowing dedicated AI seats to act without fake human control.
- **Alternatives considered**:
  - Bypass authorization for AI entirely: rejected because all actions still need explicit, reviewable authorization semantics.
  - Reuse human control rules unchanged: rejected because those rules require a human current user and cannot represent dedicated AI seats.

## Decision 8: Keep bot definitions in `BotsTable` and assignments in game metadata

- **Decision**: Continue storing global bot definitions in `BotsTable` and active per-game AI seat assignments in game metadata.
- **Rationale**: Bot definitions are shared, durable library data. Seat assignments are game-scoped orchestration metadata.
- **Alternatives considered**:
  - Store bot definitions in `UsersTable`: rejected because the library is global, not per-user.
  - Store assignments only in live presence state: rejected because restart/reload semantics require durability.

## Decision 9: Keep deterministic fallbacks for every AI-backed phase

- **Decision**: Preserve deterministic fallback behavior for region choice, purchase, auction, and forced sale even after the controller-model refactor.
- **Rationale**: The table must keep moving when OpenAI is unavailable or returns invalid choices.
- **Alternatives considered**:
  - Block the table on AI response: rejected because it would stall multiplayer play.