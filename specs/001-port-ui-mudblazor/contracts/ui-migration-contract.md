# Contract: UI Migration Behavior and Parity

## Purpose
Define the acceptance contract for migrating a UI surface from the legacy control library to MudBlazor while preserving user-visible behavior.

## Contract Scope
Applies to all migrated UI surfaces in `src/Boxcars/Components`, plus composition points (`Program.cs`, `App.razor`, `_Imports.razor`) that impact rendering and styling.

## Inputs
- Surface identifier and file path
- Baseline user journey for that surface
- Selected control mappings
- Responsive viewport targets (mobile + desktop)

## Required Checks

### 1) Behavior Parity
For each primary user action on the surface:
- Action trigger is discoverable and usable
- Resulting state matches baseline user expectation
- Validation/error/success states remain equivalent
- Navigation targets and route outcomes are unchanged

### 2) Responsive Parity
For one mobile and one desktop viewport:
- No clipped/overlapping critical controls
- Core interactions remain accessible
- Visual hierarchy remains understandable

### 3) Realtime Stability (where applicable)
For surfaces receiving live updates:
- Incoming updates do not break in-progress interactions
- Updated state is reflected without requiring full-page refresh
- No duplicate or stale interaction artifacts appear

### 4) Legacy Removal
- No legacy control components remain on validated surfaces
- No legacy CSS or asset references remain in runtime composition
- No mixed legacy/target control set remains on a single surface

## Pass/Fail Criteria
A surface passes only when all required checks pass.

## Evidence Format
Each validated surface must provide:
- Surface name/path
- Checklist results (`Pass`/`Fail`)
- Notes for any accepted visual deltas
- Follow-up actions if checks fail

## Non-Goals
- Does not define implementation code structure.
- Does not alter gameplay rule logic or multiplayer authority model.
