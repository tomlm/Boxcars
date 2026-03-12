# Feature Specification: Purchase Phase Buying and Map Analysis

**Feature Branch**: `001-purchase-railroads`  
**Created**: 2026-03-09  
**Status**: Draft  
**Input**: User description: "Purchasing of railroads Replace the stubbed out PurchaseRailroads dialog with an actual implementation. Map should be put into Rail selection mode and zoomed out. Dialog should be movable so you can see the map. Dialog should have a sorted list of unowned railroads, sorted by cost. When a railroad is selected, it should be highlighted on the map. The display should show stats for the purchasing players current network (% Accesss to cities, % monopolies of cities) Before the purchase and after the purchase. If the railroad is selected and the user selects OK, and if they have enough money, then the amount of the railroad purchase price should be removed from the players cash, and the railroad should be marked as owned by the player. They can only buy 1 railroad and only if they have enough money. The dialog should close, and then the player needs to pay their fees and then they can end their turn. I need to amend that the purchase phase also includes the option to upgrade the player engine: Express $4000, Superchief engine $40000 and the Superchief price should be set in game settings. Add map analysis. The application should analyze the loaded map data to compute the information needed for a railroad and city reference report and to support recommendation logic. The purchase experience should provide a tabbed UX with a Map tab and an Information tab. The Information tab should show the analysis report so the player can refer to it while evaluating purchases. Replace the purchase dialog with inline purchase controls: clicking an unowned railroad highlights it and shows an overlay info box with railroad price, access change, and monopoly change; the taskbar includes a synchronized combobox of railroad and engine options plus a BUY button; the combobox is sorted by price from high to low."

## Clarifications

### Session 2026-03-10

- Q: What should happen when there are no railroads the active player can buy during the purchase phase? → A: If unowned railroads remain but none are affordable, skip the dialog, show the notification "{Player} does not have enough money to purchase anything.", and proceed directly to fee payment; if no unowned railroads remain, skip the dialog without that notification.
- Q: What map state should be restored after the purchase phase ends or is skipped? → A: Zoom out and switch the map to move mode.
- Q: Which unowned railroads should appear in the purchase list when the dialog opens? → A: Show only the unowned railroads the active player can currently afford.
- Q: What should the map do when the purchase dialog is skipped? → A: Do not switch to railroad-selection mode; instead, zoom out and switch directly to move mode.
- Q: During the purchase phase, can a player both buy a railroad and buy an engine upgrade? → A: No. The player may take exactly one purchase action during the purchase phase: either buy one railroad or buy one engine upgrade.
- Amendment: Add map analysis derived from map data so the player can view a railroad and city information report and the application can use the same analysis outputs to generate recommendations. The purchase experience must provide Map and Information tabs, with the Information tab showing the analysis report.
- Amendment: Replace the purchase dialog with inline purchase controls. On the Map tab, clicking an unowned railroad highlights it and shows an overlay info box with purchase price, access change, and monopoly change. The taskbar provides a synchronized combobox of railroad and engine options plus a BUY button, and the combobox is sorted by price from high to low.
- Clarification: The sample railroad/city report numbers are illustrative. The implementation must provide the same categories of information, but it does not need to reproduce those exact sample values.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Make one purchase action (Priority: P1)

As the active player, I want to use the purchase phase to buy either one unowned railroad or one engine upgrade so I can improve my position before paying fees and ending my turn.

**Why this priority**: The purchase phase is the core turn action being defined here. Without a complete purchase flow for both railroad buying and engine upgrading, the turn cannot proceed according to the intended gameplay.

**Independent Test**: Can be fully tested by starting a turn where the active player can afford at least one valid purchase option, completing either one railroad purchase or one engine upgrade, and confirming that the chosen upgrade or ownership change and cash deduction are applied correctly before the turn continues.

**Acceptance Scenarios**:

1. **Given** it is the active player's purchase phase and the active player can afford at least one purchase option, **When** the purchase controls become available, **Then** the taskbar combobox lists the available railroad and engine options sorted from highest price to lowest price.
2. **Given** the active player has selected an unowned railroad they can afford, **When** they activate the BUY button, **Then** the railroad becomes owned by that player, the player's cash is reduced by exactly that railroad's purchase price, and the turn proceeds to fee payment before the player can end the turn.
3. **Given** the active player selects an engine upgrade they can afford, **When** they activate the BUY button, **Then** the player's engine is upgraded, the player's cash is reduced by exactly that engine upgrade's purchase price, no railroad ownership changes are made, and the turn proceeds to fee payment before the player can end the turn.
4. **Given** the active player does not have enough cash for the selected railroad or engine upgrade, **When** they attempt to confirm the purchase, **Then** the purchase is not completed and the player's cash, engine, and railroad ownership remain unchanged.
5. **Given** the active player completes one purchase action during the purchase phase, **When** the purchase result is applied, **Then** no second railroad purchase or engine upgrade may be completed during that same purchase phase.
6. **Given** unowned railroads remain but the active player cannot afford any railroad or engine upgrade option, **When** the purchase phase begins, **Then** no purchase selection is activated, the system shows the notification "{Player} does not have enough money to purchase anything.", and the turn proceeds directly to fee payment.

---

### User Story 2 - Evaluate the map impact before confirming (Priority: P2)

As the active player, I want the map and network statistics to react to my current selection so I can judge whether a railroad purchase is worth the cost.

**Why this priority**: Players need enough information to make a meaningful railroad-buying decision. Showing projected network impact is central to strategic play and reduces guesswork.

**Independent Test**: Can be fully tested by entering the Map tab during the purchase phase, selecting different available railroads from the map and the combobox, and verifying that the selection, highlight, overlay information, and projected network statistics stay synchronized.

**Acceptance Scenarios**:

1. **Given** the Map tab is active, **When** the player clicks an unowned railroad that is a current purchase option, **Then** that railroad is highlighted on the map and remains visually distinct until the selection changes or the purchase phase ends.
2. **Given** an unowned railroad is selected on the Map tab, **When** the overlay info box is shown, **Then** it displays that railroad's purchase price, access change, and monopoly change.
3. **Given** a railroad is selected from either the map or the taskbar combobox, **When** the selection changes, **Then** the map highlight, combobox selection, and projected network statistics update to match the newly selected railroad.
4. **Given** an engine upgrade is selected in the taskbar combobox, **When** the player reviews that option, **Then** the taskbar shows its purchase price and resulting engine level and does not require railroad network highlighting to complete the upgrade.

---

### User Story 3 - Review map analysis and recommendations (Priority: P3)

As the active player, I want a reference report generated from the current map so I can compare railroad value, city access, and regional probability data while deciding what to buy.

**Why this priority**: The feature now includes analysis data that supports player decision-making and recommendation logic. Without a clear, visible report, the application cannot surface the intended strategic reference information.

**Independent Test**: Can be fully tested by opening the purchase experience, switching to the Information tab, and verifying that the generated report includes railroad summary rows, city access percentages, region probabilities, and trip-level averages derived from the loaded map.

**Acceptance Scenarios**:

1. **Given** the purchase phase is available, **When** the player opens the Information tab, **Then** the system shows a map-analysis report generated from the loaded map data.
2. **Given** the Information tab is visible, **When** the report is rendered, **Then** it includes railroad summary information for each railroad, city access percentages for destinations, region probabilities, and trip-level average metrics.
3. **Given** recommendation logic needs map-derived decision inputs, **When** the application computes purchase recommendations, **Then** it uses the same underlying analysis dataset shown to the player in the Information tab.
4. **Given** the player switches from the Information tab back to the Map tab, **When** a railroad purchase option was previously selected, **Then** the current selection and its map highlight remain synchronized.

---

### User Story 4 - Keep the map visible while deciding (Priority: P4)

As the active player, I want purchase controls anchored to the map page instead of a modal dialog so I can inspect railroad geography while making my decision.

**Why this priority**: The map is essential to evaluating railroad ownership, so the interface must support visibility rather than obscuring the board.

**Independent Test**: Can be fully tested by entering the purchase phase and verifying that the map switches into railroad-selection view, zooms out to show the network, shows overlay information for selected railroads, and keeps the taskbar controls available without obscuring the map.

**Acceptance Scenarios**:

1. **Given** the purchase phase begins and at least one railroad purchase option is available, **When** the Map tab is active, **Then** the map switches into railroad-selection mode and zooms out to support whole-network review.
2. **Given** the purchase phase is active, **When** the player interacts with the taskbar combobox or the map, **Then** the purchase controls remain available without requiring a modal dialog to cover the board.
3. **Given** the purchase phase ends without a completed purchase, **When** the player returns to the turn flow, **Then** the temporary railroad-selection highlight is cleared, the map remains zoomed out, and the map switches to move mode.
4. **Given** the purchase phase is skipped because no purchase option can be selected, **When** the player returns to the turn flow, **Then** the map does not enter railroad-selection mode, the map remains zoomed out, and the map switches to move mode.
5. **Given** the purchase phase ends after a successful purchase, **When** the player returns to the turn flow, **Then** the map remains zoomed out and the map switches to move mode.

### Edge Cases

- If there are no unowned railroads and no affordable engine upgrades, the purchase step must not activate a purchase selection, must not present stale or empty controls, and must let the turn continue without recording a purchase or showing an affordability notification.
- If unowned railroads remain or engine upgrades exist but the active player cannot afford any purchase option, the purchase step must not activate a purchase selection, must show the notification "{Player} does not have enough money to purchase anything.", and must continue to fee payment without changing ownership, engine, or cash.
- If another game event makes the selected railroad unavailable before confirmation, the purchase must fail safely and require the player to choose again or exit.
- If the player leaves the purchase phase without activating BUY, no cash, engine, or ownership changes may be applied, and the map must switch to move mode while remaining zoomed out.
- If the purchase step is skipped, the map must not briefly enter railroad-selection mode before switching to move mode.
- If the player already has the highest engine upgrade available, the purchase interface must not offer a further engine upgrade.
- If the loaded map data is insufficient to compute part of the analysis report, the system must fail that analysis visibly and must not present incomplete recommendation inputs as if they were final.
- If the player switches between the Map and Information tabs, the current purchase selection state must remain intact and must not be reset solely by changing tabs.
- If the player selects a railroad on the map, the synchronized combobox selection must update to the same railroad purchase option before BUY is available.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST replace the existing railroad-purchase placeholder with a usable purchase flow during the active player's turn.
- **FR-001A**: The purchase phase MUST offer the active player eligible purchase actions for railroad buying and engine upgrading.
- **FR-001B**: If unowned railroads remain or engine upgrades exist but the active player cannot afford any purchase option, the system MUST not activate a purchase selection, MUST show the notification "{Player} does not have enough money to purchase anything.", and MUST proceed directly to the fee-payment step.
- **FR-001C**: If no unowned railroads remain and no eligible engine upgrade is available, the system MUST proceed without activating purchase controls and without showing the affordability notification.
- **FR-002**: When the purchase phase begins and at least one railroad purchase option is available, the system MUST switch the map into railroad-selection mode and present a zoom level that supports evaluation of the full railroad network.
- **FR-002A**: When the purchase phase ends, whether by successful purchase, skipped purchase, or failed confirmation followed by phase exit, the system MUST switch the map to move mode while keeping the zoomed-out view.
- **FR-002B**: When the purchase phase is skipped because no purchase option can be selected, the system MUST not switch the map into railroad-selection mode before switching to move mode.
- **FR-003**: The purchase experience MUST use inline page controls rather than a modal purchase dialog.
- **FR-003A**: The purchase experience MUST provide at least two tabs named `Map` and `Information`.
- **FR-003B**: The `Map` tab MUST provide the interactive purchase-selection experience, including railroad highlighting, synchronized selection controls, and purchase confirmation controls.
- **FR-003C**: The `Information` tab MUST show the map-analysis report for the currently loaded map.
- **FR-003D**: The taskbar MUST contain a combobox of available railroad and engine purchase options plus a BUY button.
- **FR-004**: The taskbar combobox MUST list the available railroad and engine purchase options sorted by price from highest to lowest.
- **FR-004A**: The purchase controls MUST offer eligible engine upgrade options, including Express at $4000 and Superchief at the price defined in game settings.
- **FR-004B**: The purchase controls MUST omit engine upgrades the player already owns or has surpassed.
- **FR-005**: The purchase controls MUST allow the active player to select at most one purchase option at a time.
- **FR-005A**: Selecting a railroad on the map MUST synchronize the taskbar combobox to the same purchase option.
- **FR-005B**: Selecting a railroad or engine option in the taskbar combobox MUST synchronize the active purchase selection used by the map and BUY button.
- **FR-006**: When a railroad is selected on the Map tab, the system MUST highlight that railroad on the map until the selection changes or the purchase phase ends.
- **FR-007**: The purchase controls MUST show the active player's current city-access percentage and city-monopoly percentage before the purchase.
- **FR-008**: For the currently selected railroad, the purchase controls MUST show the projected city-access percentage and projected city-monopoly percentage after the purchase.
- **FR-008H**: When an unowned railroad is selected on the map, the system MUST show an overlay info box for that railroad containing purchase price, access change, and monopoly change.
- **FR-008A**: For a selected engine upgrade, the purchase controls MUST show the upgrade's purchase price and resulting engine level.
- **FR-008B**: The system MUST analyze the loaded map data and compute the information needed to populate a railroad and city reference report.
- **FR-008C**: The railroad section of the analysis report MUST include, for each railroad, its code or short name, full name, purchase price, cities served count, city service percentage, city monopoly percentage, and railroad connection count.
- **FR-008D**: The city section of the analysis report MUST include destination access percentages derived from the loaded map data.
- **FR-008E**: The report MUST include region probability summaries for the map's destination regions.
- **FR-008F**: The report MUST include aggregate trip metrics, including average trip length, average payoff, and average payoff per dot traveled.
- **FR-008G**: The system MUST expose the same map-analysis dataset used by the Information tab to application recommendation logic without requiring the application to parse rendered report text.
- **FR-009**: The system MUST allow the active player to confirm a purchase only when a purchase option is selected and the player has at least that option's full purchase price in cash.
- **FR-010**: When the active player confirms an allowed railroad purchase, the system MUST deduct exactly the railroad's purchase price from that player's cash and assign ownership of that railroad to that player.
- **FR-010A**: When the active player confirms an allowed engine upgrade, the system MUST deduct exactly the engine upgrade's purchase price from that player's cash and update the player's engine to the selected upgrade.
- **FR-011**: The system MUST limit the purchase phase to a single purchase action per turn, either one railroad purchase or one engine upgrade.
- **FR-012**: When the active player activates BUY for a valid purchase option, the system MUST return the turn flow to the fee-payment step before the player can end the turn.
- **FR-013**: When the purchase phase ends without a successful purchase, the system MUST leave cash, railroad ownership, engine state, and network statistics unchanged.
- **FR-014**: The system MUST prevent purchase completion for railroads that are no longer unowned at the moment of confirmation.
- **FR-015**: Changing between the `Map` and `Information` tabs MUST preserve the active player's current purchase selection state.

### Key Entities *(include if feature involves data)*

- **Railroad Offering**: An unowned railroad the active player can currently afford during the current turn, including its name, purchase price, and current availability status.
- **Engine Upgrade Option**: A purchase-phase engine improvement available to the active player, including its target engine level, purchase price, and whether the player is eligible to buy it.
- **Network Coverage Snapshot**: A summary of the active player's network reach at a point in time, including percentage access to cities and percentage monopoly over cities.
- **Purchase Decision**: The active player's current purchase-phase choice, including the selected railroad or engine upgrade, whether it is affordable, and whether it can still be confirmed.
- **Railroad Overlay Info**: The map overlay shown for a selected railroad, including the railroad's purchase price, access change, and monopoly change.
- **Map Analysis Report**: A structured summary computed from the loaded map data, including railroad summary rows, city access percentages, region probabilities, and trip-level averages.
- **Railroad Analysis Row**: One railroad's analysis entry, including railroad code, full name, purchase price, cities served, service percentage, monopoly percentage, and railroad connections.
- **Recommendation Input Set**: The normalized map-analysis dataset that recommendation logic consumes when generating purchase suggestions.

## Assumptions

- Purchase actions occur only during the active player's designated purchase phase in the turn flow.
- City-access percentage and city-monopoly percentage are the authoritative network measures the game already uses for player railroad-network evaluation.
- Fee payment still occurs after the purchase phase resolves, whether or not a purchase was completed, and end-turn actions remain unavailable until that fee step has been resolved.
- Express upgrade price is fixed at $4000.
- Superchief upgrade price is controlled by game settings and defaults to $40000 unless changed there.
- When no purchase option is selectable because no option is affordable or eligible, the turn still proceeds directly to fee payment.
- The report format may be rendered differently from the sample text layout, but it must contain the same categories of map-analysis information needed for player reference and recommendation inputs.
- Recommendation outputs themselves may evolve independently, but they must be computed from the same underlying analysis dataset shown in the Information tab.
- The sample report's numeric values are examples only; correctness for this feature is based on category parity, not exact numeric matching to the sample output.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In playtesting, 100% of attempted purchase actions that meet affordability and availability rules complete with the correct cash deduction and resulting railroad ownership transfer or engine upgrade.
- **SC-002**: In playtesting, 100% of attempted purchase actions that violate affordability or availability rules are rejected without changing cash, engine state, or railroad ownership.
- **SC-003**: In usability testing, players can identify the selected railroad on the map and compare price, access change, and monopoly change for that selection without leaving the Map tab.
- **SC-004**: In turn-flow testing, 100% of successful railroad purchases and engine upgrades return the player to the fee-payment step before end-turn actions become available.
- **SC-005**: For the standard U21 map, the Information tab renders a complete railroad, city, region, and trip-metrics analysis report with no missing sections required by the spec.
- **SC-006**: Recommendation logic can consume the computed map-analysis dataset directly and produce purchase inputs without scraping or reparsing rendered UI text.
