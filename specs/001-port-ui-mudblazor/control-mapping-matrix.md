# Control Mapping Matrix

Maps legacy Fluent UI patterns to MudBlazor target patterns for consistent migration.

| MappingId | Legacy Pattern | MudBlazor Target Pattern | Behavior Notes | Coverage |
|---|---|---|---|---|
| MAP-001 | `FluentCard` | `MudCard` / `MudPaper` | Preserve section grouping and visual emphasis. | Dashboard, Profile, Map |
| MAP-002 | `FluentStack` | `MudStack` | Preserve vertical/horizontal flow and spacing semantics. | Shared across pages |
| MAP-003 | `FluentLabel` | `MudText` | Map heading/body typography variants carefully. | Shared across pages |
| MAP-004 | `FluentButton` | `MudButton` | Preserve action intent (`Accent`→`Filled/Primary`, etc.). | Shared across pages |
| MAP-005 | `FluentTextField` | `MudTextField<T>` | Preserve validation behavior and bind/update flow. | Login/Register/Profile |
| MAP-006 | `FluentSelect` + `FluentOption` | `MudSelect<T>` + `MudSelectItem<T>` | Preserve option labels, defaults, and selected value handling. | GameBoard |
| MAP-007 | `FluentListbox` | `MudList` / `MudListItem` | Preserve history/log readability and selection behavior where required. | GameBoard |
| MAP-008 | `FluentSlider` | `MudSlider<T>` | Preserve min/max/step and value-changed behavior. | GameBoard |
| MAP-009 | `FluentProgressRing` | `MudProgressCircular` | Preserve loading indicator visibility and conditional rendering. | Home/Dashboard/Profile |
| MAP-010 | `FluentDialog` family | `MudDialog` + dialog service/components | Preserve confirmation flow and blocking semantics. | Dashboard |
| MAP-011 | `FluentPersona` | `MudAvatar` + `MudText` | Preserve user identity display and fallback behavior. | Dashboard/PlayerBoard |
| MAP-012 | `FluentProfileMenu` | `MudMenu` + `MudAvatar` + `MudMenuItem` | Preserve account actions and sign-out path. | UserMenu |
| MAP-013 | Fluent namespace import | MudBlazor namespace import | Replace component namespaces globally and per-file as needed. | Global |
| MAP-014 | Fluent design theme/root assets | MudBlazor providers/assets | Preserve app-wide theming and provider behavior. | App root |

## Notes
- Choose `MudPaper` instead of `MudCard` for lightweight containers when no card-specific affordances are needed.
- Prefer MudBlazor component props/theme over custom CSS whenever possible.
- If no direct equivalent exists, use composed MudBlazor primitives and document the rationale in `migration-evidence.md`.
