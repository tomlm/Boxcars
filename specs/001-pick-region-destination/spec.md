# Feature Specification: Pick Region for Same-Region Destinations

**Feature Branch**: `[001-pick-region-destination]`  
**Created**: 2026-03-15  
**Status**: Draft  
**Input**: User description: "Pick Region When picking a city if the region of the city is the same as the current city then the user gets to pick the region they want to have a destination from. The user should be presented with access stats for each each region and they can select the region they want. Then a city is randomly selected from that region according to the city probabilities."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose a destination region after a same-region draw (Priority: P1)

As the active player, when a destination draw would keep me in the same region as my current city, I can choose the destination region used for the redraw before the final city is assigned.

**Why this priority**: This is the core rules behavior being added. Without it, same-region destination selection remains incorrect and players cannot make the required choice.

**Independent Test**: Can be fully tested by starting destination selection from a city in a known region, forcing a same-region destination draw, and verifying the game pauses for region choice instead of finalizing a city immediately.

**Acceptance Scenarios**:

1. **Given** the active player is selecting a destination and the rolled destination city belongs to the same region as the player's current city, **When** the draw is evaluated, **Then** the system presents a list of eligible destination regions and does not finalize the destination city yet.
2. **Given** the active player is presented with eligible destination regions, **When** they select one region, **Then** the system uses that chosen region as the source for the final destination city draw.
3. **Given** the active player selects their current region and the final city draw resolves to the player's current city, **When** the redraw is finalized, **Then** the system ends the player's turn without assigning an active destination.
4. **Given** the active player is choosing a replacement region, **When** the choice is pending, **Then** no turn advancement or destination finalization occurs until the region has been selected.

---

### User Story 2 - Compare regions using access statistics (Priority: P1)

As the active player, I can review access statistics for each eligible region before choosing so I can make an informed destination-region decision.

**Why this priority**: The user explicitly asked for access stats to support the decision, and the choice is materially less useful without that comparison.

**Independent Test**: Can be tested by triggering a same-region destination draw and verifying that each selectable region includes the expected access statistics before the player confirms a choice.

**Acceptance Scenarios**:

1. **Given** the active player must choose a replacement region, **When** the region-choice UI is displayed, **Then** each eligible region includes its associated access statistics in the same interaction.
2. **Given** multiple eligible regions are available, **When** the player compares them, **Then** the system presents the statistics in a consistent format so regions can be compared before selection.

---

### User Story 3 - Finalize a city using region probabilities (Priority: P2)

As the active player, once I choose a region, the final destination city is drawn from that region using the established city probabilities so the outcome remains rule-faithful.

**Why this priority**: Region selection only becomes complete when the final city assignment preserves the existing probabilistic destination rules.

**Independent Test**: Can be tested by selecting a region after a same-region draw and verifying the final destination city always belongs to that region and follows the same probability table used for standard city selection.

**Acceptance Scenarios**:

1. **Given** the player has selected a replacement destination region, **When** the destination city is finalized, **Then** the chosen city belongs to the selected region.
2. **Given** the player has selected a replacement destination region, **When** the system draws the final city, **Then** the draw uses the existing city probability rules for that region rather than a uniform or manual selection.
3. **Given** the final city has been drawn from the selected region, **When** destination selection completes, **Then** all players see the same finalized destination city.

### Edge Cases

- If the initial destination draw is already in a different region from the player's current city, the system finalizes the destination normally and does not prompt for a region choice.
- If only one eligible replacement region is available, the system still resolves destination selection correctly without producing an invalid or empty choice state.
- If a region has no valid destination cities in the probability table, that region is not offered as a selectable option.
- If the player disconnects or refreshes while a region choice is pending, the game preserves the unresolved choice so the controlling participant can resume it without drawing a new city.
- If another participant is observing the game while the active player is choosing a region, observers can see that destination selection is pending but cannot make the choice for the active player unless they are the authorized controlling participant.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST detect when a destination city draw resolves to the same region as the active player's current city.
- **FR-002**: System MUST interrupt destination finalization when a same-region draw is detected and require an eligible replacement region to be selected first.
- **FR-003**: System MUST present the active player's controlling participant with the list of eligible destination regions before the final destination city is assigned.
- **FR-004**: System MUST include the player's current region in the selectable regions when it has at least one valid destination city available for weighted selection.
- **FR-005**: System MUST exclude any region that has no valid destination cities available for weighted selection.
- **FR-006**: System MUST show access statistics for each eligible region at the time the replacement region is selected.
- **FR-007**: System MUST present the region access statistics in a consistent format that allows comparison across eligible regions.
- **FR-008**: Users MUST be able to select exactly one eligible replacement region to continue destination selection.
- **FR-009**: System MUST prevent destination resolution from completing until a valid replacement region has been selected.
- **FR-010**: System MUST draw the final destination city from the selected region using the existing city probability rules for that region.
- **FR-011**: System MUST guarantee that the finalized destination city belongs to the region selected by the player.
- **FR-012**: System MUST persist the pending region-choice state until the destination selection is fully resolved.
- **FR-013**: System MUST restore an unresolved region-choice state after reconnect or reload without changing the previously drawn same-region trigger.
- **FR-014**: System MUST allow only the active player's controlling participant to choose the replacement region.
- **FR-015**: System MUST propagate the pending region-choice state and the finalized destination result to all connected participants in real time.
- **FR-016**: System MUST preserve the normal destination-selection flow when the initially drawn city is already in a different region from the player's current city.
- **FR-017**: System MUST keep replacement-region selection and final city assignment consistent with official Rail Baron destination probabilities.
- **FR-018**: System MUST end the player's turn without assigning a destination when the selected-region redraw resolves to the player's current city.

### Key Entities *(include if feature involves data)*

- **Same-Region Destination Trigger**: The intermediate destination-selection outcome where the initially drawn city belongs to the same region as the active player's current city and therefore requires a replacement region choice.
- **Destination Region Option**: A selectable destination region offered to the player, including the region identity and its displayed access statistics.
- **Region Access Summary**: The set of access statistics shown for a region so the active player can compare destination-region options before choosing.
- **Pending Region Choice State**: The unresolved turn state that records that destination selection is waiting for a replacement region selection.
- **Final Destination Assignment**: The completed destination result containing the chosen region context and the city drawn from that region's probability distribution.

## Assumptions & Dependencies

- The existing destination-selection rules already define weighted city probabilities within each region, and this feature reuses those probabilities unchanged.
- "Access stats" refers to the same region-level access metrics already used elsewhere in the product for strategic comparison, rather than introducing a new scoring system.
- Eligible replacement regions are all destination regions that have at least one valid weighted city candidate, including the player's current region.
- Destination selection remains server-authoritative, even if the client presents the region-choice UI and statistics.
- Existing delegated-control rules apply to this decision point, so an authorized controlling participant can make the choice on behalf of the active player.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In same-region destination tests, 100% of same-region draws pause for a replacement region choice before any final destination city is assigned.
- **SC-002**: In region-choice tests, 100% of eligible region options display access statistics during the same interaction in which the player selects a region.
- **SC-003**: In destination-resolution tests, 100% of finalized cities after a replacement-region choice belong to the player-selected region.
- **SC-004**: In weighted-selection verification tests, final destination cities after region selection follow the same configured city probability distribution used by standard destination draws for that region.
- **SC-005**: In reconnect and reload tests, 100% of unresolved region-choice states are restored without silently assigning a new region or city.
- **SC-006**: In authorization tests, 100% of replacement-region choices from non-controlling participants are rejected.
