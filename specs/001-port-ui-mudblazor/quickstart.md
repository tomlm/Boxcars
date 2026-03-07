# Quickstart: UI Component Library Migration

## Prerequisites
- .NET SDK 8 installed
- Local environment can build `Boxcars.slnx`
- Working branch: `001-port-ui-mudblazor`

## 1. Baseline verification
1. From repo root, build solution:
   - `dotnet build Boxcars.slnx`
2. Run the app from `src/Boxcars`:
   - `dotnet run`
3. Manually verify baseline journeys:
   - Landing page
   - Sign-in/account flow
   - Dashboard flow
   - Game board entry and key interactions

## 2. Migration execution order
1. Infrastructure migration
   - Replace legacy package references with MudBlazor in `src/Boxcars/Boxcars.csproj`
   - Update service registration in `src/Boxcars/Program.cs`
   - Update root assets/providers in `src/Boxcars/Components/App.razor`
   - Update global UI imports in `src/Boxcars/Components/_Imports.razor`
2. Shared shell/layout migration
   - Migrate main layout and shared nav/profile patterns
3. Page/component migration batches
   - Batch A: landing/account/dashboard pages
   - Batch B: map/game board surfaces and child components
4. Styling cleanup
   - Remove obsolete legacy styles
   - Keep only required custom CSS not replaceable by MudBlazor theme/layout

## 3. Verification checklist
After each batch:
- `dotnet build Boxcars.slnx` passes
- Primary journey for touched surfaces passes
- Responsive check passes at one mobile and one desktop viewport
- No newly introduced legacy references in touched files

## 4. Final acceptance
Before completing feature:
1. Search for remaining legacy references in source (exclude generated outputs):
   - Verify no legacy package references in `src/Boxcars/Boxcars.csproj`
   - Verify no legacy component namespaces/usages in `src/Boxcars/Components/**`
2. Re-run build:
   - `dotnet build Boxcars.slnx`
3. Execute final manual parity sweep across all primary journeys.

## Troubleshooting
- If providers/services are missing, re-check root composition and DI registration before page-level debugging.
- If layouts regress, prefer component layout primitives/theme settings before adding custom CSS.
- If realtime behavior regresses, validate hub updates on migrated surfaces and compare against baseline interaction flow.
