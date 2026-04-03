# Feature Specification: Better Routing for Unfriendly Destinations

**Feature Branch**: `[005-better-unfriendly-routing]`  
**Created**: 2026-04-02  
**Status**: Draft  
**Input**: User description: "Better Routing for Unfriendly destinations. I want to make the route planning more sophisticated for Unfriendly Destinations."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose the cheapest unfriendly-destination plan, not just the cheapest arrival path (Priority: P1)

As a player headed to an unfriendly destination, I want the suggested route to minimize the total expected fee exposure for reaching the destination and starting the next turn so I do not get trapped into avoidable unfriendly payments.

**Why this priority**: This is the core gameplay value. The feature exists to make route planning smarter specifically when a destination forces unfriendly fees.

**Independent Test**: Can be fully tested by constructing destinations served only by unfriendly railroads and verifying the chosen route minimizes total expected cost across arrival plus post-arrival exit setup.

**Acceptance Scenarios**:

1. **Given** a destination city is serviced only by unfriendly railroads, **When** route suggestion runs, **Then** the system evaluates candidate routes using the combined fee outlook for reaching the destination and beginning the following turn.
2. **Given** two candidate routes have different arrival costs but one leaves the player with a cheaper post-arrival exit plan, **When** route suggestion runs, **Then** the system selects the route with the lower combined cost outlook rather than the lower immediate-arrival cost alone.
3. **Given** one candidate route reaches a friendly or public railroad closer to the unfriendly destination before the final approach, **When** that route lowers the combined cost outlook, **Then** the system prefers that route.

---

### User Story 2 - Account for bonus-out opportunities after reaching an unfriendly destination (Priority: P2)

As a player using an `Express` or `Superchief`, I want route suggestion to account for bonus-roll opportunities that may let me exit the unfriendly destination area without paying another unfriendly fee.

**Why this priority**: Bonus movement materially changes the best route choice for unfriendly destinations and is explicitly part of the game rules and user strategy.

**Independent Test**: Can be tested by comparing otherwise-equal unfriendly-destination routes where one path has a better chance to bonus out to friendly/public track after arrival.

**Acceptance Scenarios**:

1. **Given** the active locomotive and authoritative turn rules allow a bonus move after arrival, **When** route suggestion evaluates an unfriendly destination, **Then** it includes the possibility of bonus movement when estimating post-arrival fee exposure.
2. **Given** two equal-cost arrival paths differ only in their chance to reach friendly or public track during bonus movement, **When** route suggestion runs, **Then** it prefers the path with the better bonus-out outlook.
3. **Given** the active locomotive cannot receive a bonus move, **When** route suggestion runs, **Then** bonus-out analysis does not affect route ranking.

---

### User Story 3 - Prefer strategically better fee recipients when costs tie (Priority: P3)

As a player forced to pay unfriendly fees, I want the route suggestion to prefer paying opponents who have the least cash or weakest network, and to spread repeated unavoidable unfriendly payments across owners when the total cost is identical.

**Why this priority**: When fee totals are equal, the strategic value comes from weakening stronger opponents less and avoiding repeated payments to the same owner.

**Independent Test**: Can be tested by constructing tied candidate routes with identical travel cost but different unfriendly fee recipients and verifying the route selection uses the specified strategic tie breaks.

**Acceptance Scenarios**:

1. **Given** two candidate routes have identical combined cost outlooks, **When** one route pays an owner with less cash or a weaker network, **Then** the system prefers that route.
2. **Given** two candidate routes have identical combined cost outlooks and require multiple unavoidable unfriendly payments, **When** one route spreads those payments across multiple owners, **Then** the system prefers the spread-payment route over repeatedly paying the same owner.
3. **Given** tied candidates remain indistinguishable after strategic tie breaks, **When** route suggestion runs, **Then** the system resolves the tie deterministically.

---

### Edge Cases

- A destination city may be unfriendly only at the destination itself while nearby approach paths remain friendly or public.
- A destination may be reachable through multiple unfriendly railroads owned by different players with different cash and network strength.
- A destination may require paying unfriendly fees both on arrival and again when leaving on the next turn.
- A player may arrive at an unfriendly destination with a bonus move available but no reachable friendly/public exit within legal bonus movement.
- Grandfathered fee rules may reduce the effective unfriendly fee for a specific railroad after a purchase or sale.
- All railroads may be sold, which changes unfriendly fees from the first configured rate to the second configured rate.
- A player may already be on an unfriendly railroad before the final approach, making the best plan dependent on whether another unfriendly fee would be triggered by switching or by starting the next turn.
- Two or more candidate routes may tie on combined expected cost, bonus-out outlook, owner cash preference, and network preference.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST identify an unfriendly destination as a destination city whose servicing railroads are all unfriendly to the active player.
- **FR-002**: System MUST evaluate route suggestions for unfriendly destinations using a combined outlook that includes reaching the destination and the fee risk of starting the following turn.
- **FR-003**: System MUST continue to use authoritative game settings for fee values, including `PublicFee`, `PrivateFee`, `UnfriendlyFee1`, and `UnfriendlyFee2`.
- **FR-004**: System MUST treat personal railroads and public railroads as the lower-fee categories defined by current game settings rather than hard-coded values.
- **FR-005**: System MUST apply grandfathered-fee rules when a railroad’s effective fee is reduced by ownership-change history.
- **FR-006**: System MUST evaluate the cost of entering an unfriendly destination area based on the actual railroad transitions and turn boundaries that trigger fees under the authoritative engine rules.
- **FR-007**: System MUST evaluate the player’s first-step exit outlook after reaching an unfriendly destination, including the cost of remaining trapped on unfriendly railroads at the start of the next turn.
- **FR-008**: System MUST account for authoritative locomotive bonus-move rules when estimating whether a player can reach friendly or public track after arriving at an unfriendly destination (`BonusOut`).
- **FR-009**: System MUST prefer the candidate route with the lowest combined post-arrival fee outlook for unfriendly destinations.
- **FR-010**: System MUST prefer candidate routes that improve the chance of reaching the destination from friendly/public track on the next turn when that lowers the combined outlook.
- **FR-011**: When combined cost outlooks are equal, system MUST prefer candidate routes whose unavoidable unfriendly fees benefit opponents with lower cash totals.
- **FR-012**: When combined cost outlooks remain equal, system MUST define "weaker network" as lower `AccessibleDestinationPercent`, with lower `MonopolyDestinationPercent` used as the next tie break when needed.
- **FR-013**: When combined cost outlooks remain equal and multiple unfriendly payments are unavoidable, system MUST prefer routes that spread those payments across multiple owners rather than repeatedly paying the same owner.
- **FR-014**: System MUST use deterministic tie breaking after all cost and strategic preferences are exhausted.
- **FR-015**: System MUST keep route suggestion advisory-only and MUST NOT mutate authoritative game state during evaluation.
- **FR-016**: System MUST expose enough route-suggestion detail for the UI to explain why an unfriendly-destination route was chosen, including destination-entry and post-arrival fee outlook components.
- **FR-017**: System MUST preserve existing no-route and invalid-route failure handling.
- **FR-018**: System MUST support focused regression tests covering unfriendly-destination arrival cost, next-turn exit planning, bonus-out evaluation, grandfathered fees, and strategic tie breaks.
- **FR-019**: For unfriendly destinations, system MUST rank candidate routes by lowest expected total fee exposure first, then by lowest worst-case next-turn fee exposure.
- **FR-020**: System MUST evaluate `BonusOut` using an exact probability model derived from authoritative locomotive, movement, and bonus-roll rules rather than a simple reachable/not-reachable heuristic.

### Key Entities *(include if feature involves data)*

- **Unfriendly Destination**: A destination-city condition where every railroad serving the destination is unfriendly to the active player.
- **Unfriendly Route Outlook**: The advisory evaluation for a candidate route, including arrival fee triggers, post-arrival exit exposure, and bonus-out opportunity.
- **BonusOut Evaluation**: The estimated ability for the active player to reach friendly or public track after arrival using authoritative bonus-move rules.
- **Fee Recipient Profile**: The strategic snapshot of an opposing railroad owner used for tie breaks, including cash position and network strength.
- **Payment Spread Pattern**: The distribution of unavoidable unfriendly fee payments across one or more railroad owners for a candidate route.

## Assumptions & Dependencies

- Existing route suggestion remains an advisory calculation performed from authoritative map and game-state inputs.
- Current fee semantics for public, personal, unfriendly, and grandfathered railroads are already defined by the authoritative engine and settings model.
- Existing network-strength calculations can be reused for "worst network" comparisons.
- Existing locomotive and bonus-roll rules remain the authoritative source for bonus-out eligibility and movement capacity.
- The same route suggestion surface in `GameEngine`, `MapRouteService`, and related UI projections will remain the integration point for this behavior.

## Design Decisions

- **DD-001**: "Worst network" is defined as lower `AccessibleDestinationPercent`, with lower `MonopolyDestinationPercent` used as the next tie break.
- **DD-002**: The post-arrival optimization target is lexicographic: lowest expected total fee exposure first, then lowest worst-case next-turn fee exposure.
- **DD-003**: `BonusOut` evaluation uses an exact probability model based on authoritative locomotive and bonus-roll rules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In controlled unfriendly-destination scenarios, 100% of route suggestions choose the route with the lowest defined combined arrival-plus-exit fee outlook.
- **SC-002**: In controlled scenarios where bonus movement changes the preferred route, 100% of suggestions match the expected bonus-aware route choice.
- **SC-003**: In controlled tie scenarios with equal cost outlooks, 100% of suggestions apply the configured strategic tie breaks consistently and deterministically.
- **SC-004**: Regression coverage includes representative tests for grandfathered-fee handling, all-railroads-sold unfriendly fee escalation, next-turn exit planning, and owner-spread tie breaks.

## Rulebook Reference

- Fee evaluation MUST align with the official Rail Baron rules governing public, personal, unfriendly, and post-purchase/post-sale fee behavior.
- Bonus movement analysis MUST align with the official Rail Baron rules governing locomotive bonus behavior.
- If the digital implementation needs a deterministic interpretation for expected-cost routing, that interpretation MUST be documented and applied consistently across engine and advisory outputs.
