# UI Surface Inventory

Tracks migration state per UI surface for feature `001-port-ui-mudblazor`.

## Legend
- `Type`: `Composition` | `Layout` | `Page` | `Component` | `Model`
- `Migration`: `NotStarted` | `InProgress` | `Migrated` | `Validated`
- `Parity`: `Unknown` | `Pass` | `Fail`
- `Responsive`: `Unknown` | `Pass` | `Fail`
- `LegacyUsageDetected`: `Yes` | `No`

## Foundation Surfaces

| SurfaceId | Type | Location | Migration | Parity | Responsive | LegacyUsageDetected |
|---|---|---|---|---|---|---|
| SF-001 | Composition | src/Boxcars/Boxcars.csproj | NotStarted | Unknown | Unknown | Yes |
| SF-002 | Composition | src/Boxcars/Program.cs | NotStarted | Unknown | Unknown | Yes |
| SF-003 | Composition | src/Boxcars/Components/App.razor | NotStarted | Unknown | Unknown | Yes |
| SF-004 | Composition | src/Boxcars/Components/_Imports.razor | NotStarted | Unknown | Unknown | Yes |
| SF-005 | Model | src/Boxcars/Data/PlayerBoardModel.cs | NotStarted | Unknown | Unknown | Yes |

## Shared Shell Surfaces

| SurfaceId | Type | Location | Migration | Parity | Responsive | LegacyUsageDetected |
|---|---|---|---|---|---|---|
| SH-001 | Layout | src/Boxcars/Components/Layout/MainLayout.razor | NotStarted | Unknown | Unknown | Yes |
| SH-002 | Layout | src/Boxcars/Components/Layout/MainLayout.razor.css | NotStarted | Unknown | Unknown | Yes |
| SH-003 | Component | src/Boxcars/Components/Layout/UserMenu.razor | NotStarted | Unknown | Unknown | Yes |
| SH-004 | Composition | src/Boxcars/Components/Routes.razor | NotStarted | Unknown | Unknown | Unknown |

## Page Surfaces

| SurfaceId | Type | Location | Migration | Parity | Responsive | LegacyUsageDetected |
|---|---|---|---|---|---|---|
| PG-001 | Page | src/Boxcars/Components/Pages/Home.razor | NotStarted | Unknown | Unknown | Yes |
| PG-002 | Page | src/Boxcars/Components/Pages/Dashboard.razor | NotStarted | Unknown | Unknown | Yes |
| PG-003 | Page | src/Boxcars/Components/Pages/ProfileSettings.razor | NotStarted | Unknown | Unknown | Yes |
| PG-004 | Page | src/Boxcars/Components/Pages/GameBoard.razor | NotStarted | Unknown | Unknown | Yes |
| PG-005 | Page | src/Boxcars/Components/Pages/GameBoard.razor.css | NotStarted | Unknown | Unknown | Yes |

## Map Component Surfaces

| SurfaceId | Type | Location | Migration | Parity | Responsive | LegacyUsageDetected |
|---|---|---|---|---|---|---|
| MP-001 | Component | src/Boxcars/Components/Map/GameMapComponent.razor | NotStarted | Unknown | Unknown | Unknown |
| MP-002 | Component | src/Boxcars/Components/Map/MapComponent.razor | NotStarted | Unknown | Unknown | Yes |
| MP-003 | Component | src/Boxcars/Components/Map/MapComponent.razor.css | NotStarted | Unknown | Unknown | Yes |
| MP-004 | Component | src/Boxcars/Components/Map/PlayerBoard.razor | NotStarted | Unknown | Unknown | Yes |
| MP-005 | Component | src/Boxcars/Components/Map/PlayerBoard.razor.css | NotStarted | Unknown | Unknown | Yes |

## Account Surfaces (Current Scope)

| SurfaceId | Type | Location | Migration | Parity | Responsive | LegacyUsageDetected |
|---|---|---|---|---|---|---|
| AC-001 | Component | src/Boxcars/Components/Account/Shared/AccountLayout.razor | NotStarted | Unknown | Unknown | Unknown |
| AC-002 | Component | src/Boxcars/Components/Account/Shared/ManageLayout.razor | NotStarted | Unknown | Unknown | Unknown |
| AC-003 | Component | src/Boxcars/Components/Account/Shared/ManageNavMenu.razor | NotStarted | Unknown | Unknown | Unknown |
| AC-004 | Page | src/Boxcars/Components/Account/Pages/Login.razor | NotStarted | Unknown | Unknown | Unknown |
| AC-005 | Page | src/Boxcars/Components/Account/Pages/Register.razor | NotStarted | Unknown | Unknown | Unknown |
| AC-006 | Page | src/Boxcars/Components/Account/Pages/ResetPassword.razor | NotStarted | Unknown | Unknown | Unknown |

## Notes
- `LegacyUsageDetected` should be updated to `No` only after code-level verification confirms no legacy component or asset references remain for that surface.
- `Validated` requires parity and responsive checks to both pass.
