# Migration Evidence Log

Feature: `001-port-ui-mudblazor`

## Build Verification
| Date | Command | Result | Notes |
|---|---|---|---|
| 2026-03-06 | `dotnet build Boxcars.slnx` | Pass | Foundational mixed-mode migration compiles after scoped Mud namespace imports. |
| 2026-03-06 | `dotnet build Boxcars.slnx` | Pass | Compile confirmed after removing Fluent type dependency from `PlayerBoardModel`. |
| 2026-03-06 | `dotnet build Boxcars.slnx` | Pass | Compile confirmed after Dashboard/ProfileSettings/GameBoard MudBlazor migrations. |
| 2026-03-06 | `dotnet build Boxcars.slnx` | Pass | Compile confirmed after MapComponent/PlayerBoard/GameMapComponent migrations. |
| 2026-03-06 | `dotnet build Boxcars.slnx` | Pass | Compile confirmed after Account layout/nav and Login/Register/ResetPassword migrations. |

## Legacy Removal Verification
| Date | Scope | Check | Result | Notes |
|---|---|---|---|---|
| 2026-03-06 | US1 migrated surfaces (`Routes`, `Home`, `Dashboard`, `ProfileSettings`, `GameBoard`, `MapComponent`, `PlayerBoard`, `GameMapComponent`, `AccountLayout`, `ManageLayout`, `ManageNavMenu`, `Login`, `Register`, `ResetPassword`) | `Select-String` scan for `Fluent|Microsoft.FluentUI` | Pass | No legacy references found in migrated surface files. |

## Behavior Parity Verification (US1)
| Date | Journey | Result | Notes |
|---|---|---|---|
| 2026-03-07 | Landing page | Pending re-verify | User reported Loading/page flashing loop; Home redirect moved from `OnInitialized` to first-render navigation to prevent repeat redirect cycle. Awaiting runtime confirmation. |
| 2026-03-07 | Sign-in/account flow | Pass | Manual runtime verification confirmed by user after latest interaction/render-mode fixes. |
| 2026-03-07 | Dashboard flow | Pass | Manual runtime verification confirmed by user; profile icon menu interaction restored and opening correctly. |
| 2026-03-07 | Game board entry & key actions | Pending manual | Remaining US1 parity checkpoint to complete T029. |

## CSS Minimization Verification (US2)
| Date | Surface | Pre-rule Count | Post-rule Count | Result | Rationale for Remaining CSS |
|---|---|---:|---:|---|---|
| 2026-03-06 | Dashboard (`Components/Pages/Dashboard.razor`) | N/A | N/A | Pass | Inline style attributes removed; layout/actions now use MudBlazor components with isolated CSS classes. |
| 2026-03-06 | Profile Settings (`Components/Pages/ProfileSettings.razor`) | N/A | N/A | Pass | Inline style attributes removed; form/action layout uses MudBlazor components with isolated CSS classes. |
| 2026-03-06 | Game Board (`Components/Pages/GameBoard.razor.css`) | N/A | N/A | Pass | Fluent-specific deep selectors and tokens replaced with direct classes and Mud palette tokens where applicable. |
| 2026-03-06 | Map Component (`Components/Map/MapComponent.razor.css`) | N/A | N/A | Pass | Menu and text tokens now use Mud palette variables; remaining CSS handles SVG/map interaction geometry not covered by Mud components. |
| 2026-03-06 | Player Board (`Components/Map/PlayerBoard.razor.css`) | N/A | N/A | Pass | Fluent host selectors removed; remaining CSS handles domain-specific player color borders and compact card density. |
| 2026-03-06 | Main Layout (`Components/Layout/MainLayout.razor.css`) | N/A | N/A | Pass | Remaining CSS is structural shell spacing and global Blazor error banner styling not provided by component props alone. |

## Responsive Verification (US3)
Reference checklist: `responsive-checklist.md`

| Date | Surface Group | Mobile | Desktop | Result | Notes |
|---|---|---|---|---|---|

## Open Issues
| Date | Area | Description | Severity | Status |
|---|---|---|---|---|
