# Feature Specification: Game Creation Settings

**Feature Branch**: `001-game-settings`  
**Created**: 2026-03-21  
**Status**: Draft  
**Input**: User description: "Add Game Settings. When a game is created there should be a settings page. Start with immutable game settings persisted in the Game entity and used by all game logic, including starting cash, announcing cash, winning cash, rover cash, public and private fees, unfriendly fees, home swapping, home city choice, cash secrecy, starting engine, and engine prices."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure a game before play begins (Priority: P1)

As the player creating a game, I want a dedicated settings step during game creation so I can decide which rule values this match will use before anyone starts playing.

**Why this priority**: If the settings cannot be chosen before the game begins, the game cannot support rule variants or visibly confirm which values govern the match.

**Independent Test**: Can be fully tested by creating a new game, reviewing the default values, changing one or more settings, saving the game, and verifying the created game shows the selected settings before play starts.

**Acceptance Scenarios**:

1. **Given** a player is creating a new game, **When** they reach the settings step, **Then** the system shows all supported settings with their current default values.
2. **Given** a player changes one or more settings before saving the game, **When** the game is created, **Then** the system stores the selected values as part of that game's permanent setup.
3. **Given** a player does not change any settings, **When** the game is created, **Then** the system stores the documented default values for that game.

---

### User Story 2 - Lock rule values once gameplay starts (Priority: P2)

As a player in the game, I want the chosen settings to become immutable when the game starts so the rules cannot shift mid-match.

**Why this priority**: Rail Baron outcomes depend on stable cash thresholds, fees, and engine pricing. Allowing them to change after play begins would undermine fairness and trust.

**Independent Test**: Can be fully tested by creating a game, starting play, and verifying that the saved settings remain visible but are no longer editable through normal game flows.

**Acceptance Scenarios**:

1. **Given** a game is still in pre-start setup, **When** the creator reviews the settings, **Then** the game still reflects the saved values chosen for that match.
2. **Given** a game has started, **When** any player accesses the game setup details, **Then** the settings are shown as fixed values and cannot be changed.
3. **Given** gameplay has begun, **When** a player attempts any settings-changing path, **Then** the system prevents the change and preserves the original values.

---

### User Story 3 - Have gameplay honor the saved settings (Priority: P3)

As a player participating in a match, I want every rule that depends on cash thresholds, fees, secrecy, home selection, or engine availability to use that game's saved settings so the game behaves consistently with the chosen setup.

**Why this priority**: The settings page has no value unless all affected game behavior uses the saved settings instead of hard-coded defaults.

**Independent Test**: Can be fully tested by starting games with non-default values and verifying that cash visibility, declaration thresholds, rover awards, fee calculations, home selection options, and starting engine behavior all follow the saved settings.

**Acceptance Scenarios**:

1. **Given** a game was created with non-default cash thresholds and fee values, **When** gameplay reaches those rule checks, **Then** the system uses the saved settings for that specific game.
2. **Given** a game was created with `KeepCashSecret` enabled, **When** players review another player's cash before that player reaches the declaration threshold, **Then** they do not see the exact amount.
3. **Given** a game was created with `KeepCashSecret` disabled, **When** players review another player's cash, **Then** they see the exact amount throughout the game.
4. **Given** a game was created with `HomeSwapping` enabled, **When** a player has chosen home and first destination, **Then** that player is allowed to swap them before continuing.
5. **Given** a game was created with `HomeCityChoice` disabled, **When** a player selects a home region, **Then** the system assigns the home city according to the game's non-choice flow rather than offering a city picker.

### Edge Cases

- If a numeric setting is missing during game creation, the system must apply the documented default value before the game is saved.
- If a numeric setting is provided outside its allowed range, the system must reject the invalid value and keep the player in the setup flow.
- If `WinningCash` is lower than `AnnouncingCash`, the system must prevent saving because the declaration and win thresholds would conflict.
- If an engine price is set to zero or lower, the system must prevent saving because that engine option would not have a valid cost.
- If `StartEngine` references an engine option outside the supported set, the system must prevent saving and require a valid option.
- If `HomeCityChoice` is enabled and every city in the selected region is already used as a home city, the system must not offer an already-claimed city.
- If `KeepCashSecret` is enabled and a player crosses above the announcing threshold after previously being below it, the system must make that status visible to all players from that point onward while the player remains above the threshold.
- If `KeepCashSecret` is enabled and a player's cash falls back below the announcing threshold after having been public, the system must return that player's cash display to the concealed view.
- If an existing game created before this feature has no explicit settings recorded, the system must continue to behave as though the documented defaults were in effect for that game.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a settings page or step as part of new game creation.
- **FR-002**: The game creation settings experience MUST expose the following settings: `StartingCash`, `AnnouncingCash`, `WinningCash`, `RoverCash`, `PublicFee`, `PrivateFee`, `UnfriendlyFee1`, `UnfriendlyFee2`, `HomeSwapping`, `HomeCityChoice`, `KeepCashSecret`, `StartEngine`, `SuperchiefPrice`, and `ExpressPrice`.
- **FR-003**: The system MUST default `StartingCash` to 20,000 for newly created games.
- **FR-004**: The system MUST default `AnnouncingCash` to 250,000 for newly created games.
- **FR-005**: The system MUST default `WinningCash` to 300,000 for newly created games.
- **FR-006**: The system MUST default `RoverCash` to 50,000 for newly created games.
- **FR-007**: The system MUST default `PublicFee` to 1,000 for newly created games.
- **FR-008**: The system MUST default `PrivateFee` to 1,000 for newly created games.
- **FR-009**: The system MUST default `UnfriendlyFee1` to 5,000 for newly created games.
- **FR-010**: The system MUST default `UnfriendlyFee2` to 10,000 for newly created games.
- **FR-011**: The system MUST default `HomeSwapping` to enabled for newly created games.
- **FR-012**: The system MUST default `HomeCityChoice` to enabled for newly created games.
- **FR-013**: The system MUST default `KeepCashSecret` to enabled for newly created games.
- **FR-014**: The system MUST default `StartEngine` to `Freight` for newly created games.
- **FR-015**: The system MUST default `SuperchiefPrice` to 40,000 for newly created games.
- **FR-016**: The system MUST default `ExpressPrice` to 4,000 for newly created games.
- **FR-017**: The system MUST save the selected settings as part of the game's permanent data when the game is created.
- **FR-018**: Each saved game MUST retain its own settings values independently of other games.
- **FR-019**: The system MUST treat the saved settings as immutable once the game has started.
- **FR-020**: The system MUST prevent players from modifying saved settings after the game has started.
- **FR-021**: The system MUST continue to display the saved settings after the game has started so players can confirm which rules govern the match.
- **FR-022**: All gameplay rules that depend on starting money, announcement thresholds, winning thresholds, rover awards, railroad fees, home-selection rules, cash visibility, starting engine, or engine upgrade prices MUST use the saved settings for that game.
- **FR-023**: The system MUST use `StartingCash` when determining each player's opening cash amount.
- **FR-024**: The system MUST use `AnnouncingCash` when determining when a player's cash amount becomes publicly announced.
- **FR-025**: The system MUST use `WinningCash` when determining whether a player may declare victory and whether they have met the cash requirement to win.
- **FR-026**: The system MUST use `RoverCash` when awarding money for roving a declared player.
- **FR-027**: The system MUST use `PublicFee`, `PrivateFee`, `UnfriendlyFee1`, and `UnfriendlyFee2` when calculating route fees owed during play.
- **FR-028**: The system MUST use `HomeSwapping` to determine whether players may swap home and first destination after both are chosen.
- **FR-029**: The system MUST use `HomeCityChoice` to determine whether players may choose a city within the selected home region.
- **FR-030**: When `HomeCityChoice` is enabled, the system MUST prevent players from selecting a city already used as another player's home city.
- **FR-031**: The system MUST use `KeepCashSecret` and `AnnouncingCash` together to determine what cash information other players can see for each player during the game.
- **FR-032**: When `KeepCashSecret` is enabled, the system MUST show other players the exact cash amount for any player whose cash is at or above `AnnouncingCash`.
- **FR-032A**: When `KeepCashSecret` is enabled, the system MUST return that player's cash display to the concealed view if the player's cash later falls below `AnnouncingCash`.
- **FR-033**: The system MUST use `StartEngine` to determine the engine each player begins the game with.
- **FR-034**: The system MUST restrict `StartEngine` to one of these supported values: `Freight`, `Express`, or `Superchief`.
- **FR-035**: The system MUST use `ExpressPrice` and `SuperchiefPrice` when determining the cash cost to upgrade to those engines during play.
- **FR-036**: The system MUST validate settings before game creation is completed.
- **FR-037**: The system MUST require all numeric settings to be positive whole-dollar amounts.
- **FR-038**: The system MUST require `WinningCash` to be greater than or equal to `AnnouncingCash`.
- **FR-039**: The system MUST preserve documented default behavior for legacy games that do not yet have explicit saved settings.

### Key Entities *(include if feature involves data)*

- **Game Settings**: The full immutable ruleset chosen for one game, including all cash thresholds, fee values, engine values, and boolean rule toggles.
- **Game**: A single multiplayer match that owns exactly one settings set governing its lifecycle.
- **Fee Schedule**: The portion of game settings that determines the amount owed for public, private, and unfriendly railroad travel.
- **Cash Visibility Rule**: The portion of game settings that determines whether exact player cash is public or concealed before the declaration threshold is reached.
- **Engine Rule Set**: The portion of game settings that determines the starting engine and the upgrade prices available during play.
- **Home Selection Rule Set**: The portion of game settings that determines whether players may choose a city within a home region and whether they may swap home and first destination.

## Assumptions & Dependencies

- A game has a distinct pre-start phase during which the creator can finish setup before live gameplay begins.
- Starting a game is the transition point after which rule values become immutable for that match.
- Existing games created before this feature may not yet have stored settings and therefore must continue to behave using the documented default values.
- Cash visibility behavior continues to follow the current Rail Baron interpretation already used by Boxcars, with this feature only replacing hard-coded thresholds and secrecy toggles with saved game settings.
- Only the supported engine options `Freight`, `Express`, and `Superchief` are in scope for this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In game-creation validation tests, 100% of newly created games persist a complete settings set, whether the creator accepts defaults or customizes values.
- **SC-002**: In gameplay validation tests, 100% of opening cash, declaration checks, win checks, rover awards, route fees, home-selection choices, cash-visibility behavior, and engine pricing outcomes match the saved settings for that game.
- **SC-003**: In post-start validation tests, 100% of attempts to change settings after gameplay begins are rejected and leave the original settings unchanged.
- **SC-004**: In legacy-game compatibility tests, 100% of pre-existing games without stored settings continue to use the documented default rule values.
- **SC-005**: In setup usability tests, a player creating a game can review and confirm all supported settings for that match in under 2 minutes.
