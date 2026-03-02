# Research: BoxCars Shell Application Pages

**Feature**: `002-shell-app-pages`  
**Date**: 2026-02-27  
**Purpose**: Resolve all technical unknowns for Azure Table Storage + Fluent UI Blazor architecture.

---

## R1: Custom Identity Stores Backed by Azure Table Storage

**Context**: The default Blazor Server template uses EF Core + SQL Server for ASP.NET Core Identity. The project requires Azure Table Storage exclusively — no SQL Server, no EF Core. Identity needs custom store implementations.

**Decision**: Implement `IUserStore<ApplicationUser>`, `IUserEmailStore<ApplicationUser>`, `IUserPasswordStore<ApplicationUser>`, `IUserSecurityStampStore<ApplicationUser>`, and `IUserLockoutStore<ApplicationUser>` backed by Azure Table Storage via `Azure.Data.Tables`. Register these custom stores instead of `AddEntityFrameworkStores<>`. Remove EF Core packages and `ApplicationDbContext`.

**Rationale**: ASP.NET Core Identity is designed for pluggable storage. The `IUserStore<T>` family of interfaces is the documented extension point. Identity user records are simple key-value data (email, password hash, security stamp, lockout info) — a natural fit for table storage. This eliminates SQL Server and EF Core entirely.

**Key patterns**:
```csharp
// ApplicationUser — no longer extends IdentityUser; standalone POCO mapped to table storage
public class ApplicationUser : ITableEntity
{
    public string PartitionKey { get; set; } = "USER";
    public string RowKey { get; set; } = string.Empty; // User ID (GUID)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Id => RowKey;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
}

// Custom store registration in Program.cs
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    options.SignIn.RequireConfirmedAccount = true)
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IUserStore<ApplicationUser>, TableStorageUserStore>();
```

**Table**: `UsersTable` (mono table per constitution naming)

**Lookup patterns**: 
- By ID: PartitionKey="USER", RowKey=userId
- By email: Secondary index via `UserEmailIndexTable` (PartitionKey="EMAIL_INDEX", RowKey=normalizedEmail, Value=userId)
- By username: Secondary index via `UserNameIndexTable` (PartitionKey="USERNAME_INDEX", RowKey=normalizedUserName, Value=userId)

**NuGet changes**:
- Remove: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Tools`, `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore`
- Add: `Azure.Data.Tables`

**Alternatives considered**:
- Keep EF Core for Identity only: Introduces SQL Server dependency for a few simple records. User explicitly rejected this.
- SQLite for Identity: Still relational, still EF Core dependency chain. Doesn't match production storage strategy.

---

## R2: ApplicationUser as ITableEntity with Player Profile Fields

**Context**: `ApplicationUser` needs Identity fields (email, password hash, etc.) plus BoxCars profile fields (Nickname, ThumbnailUrl). All stored in Azure Table Storage.

**Decision**: Make `ApplicationUser` implement `ITableEntity` directly. Include all Identity-required properties plus `Nickname` and `ThumbnailUrl` as flat properties. Single table row per user.

**Rationale**: Azure Table Storage stores flat entities — no relational joins. Putting all user data in one entity avoids a second lookup for profile fields. The `Azure.Data.Tables` SDK serializes all public properties automatically.

**Alternatives considered**:
- Separate `PlayerProfilesTable`: Extra round-trip for every dashboard load just to get nickname/thumbnail. Unnecessary complexity for two extra fields.

---

## R3: Nickname Uniqueness via Index Table

**Context**: Nicknames must be globally unique (clarification). Need O(1) uniqueness check and conflict detection during profile updates.

**Decision**: Add a `NicknameIndexTable` (mono table). PartitionKey="NICKNAME_INDEX", RowKey=normalizedNickname (uppercased), UserId=owning user's ID. On nickname change, delete old index entry and insert new one. If insert fails (409 Conflict), the nickname is taken.

**Rationale**: Same proven pattern as `UserEmailIndexTable` and `UserNameIndexTable`. Table Storage provides entity-level conflict detection on insert. No scanning required.

**Key patterns**:
```csharp
// Check uniqueness before saving
try
{
    await nicknameIndexTable.AddEntityAsync(new IndexEntity
    {
        PartitionKey = "NICKNAME_INDEX",
        RowKey = normalizedNickname,
        UserId = userId
    }, cancellationToken);
}
catch (RequestFailedException ex) when (ex.Status == 409)
{
    // Nickname already taken
    return NicknameResult.Conflict;
}
```

**Alternatives considered**:
- Query filter on UsersTable for NormalizedNickname: O(n) scan — doesn't scale and has race conditions.

---

## R4: Auto-Provisioning Player Profile on First Login

**Context**: When a user registers and logs in for the first time, `Nickname` and `ThumbnailUrl` should be auto-populated with defaults if empty.

**Decision**: Populate defaults during account creation in the Register flow. When `UserManager.CreateAsync` is called, set `Nickname` from email prefix and `ThumbnailUrl` to a default placeholder URL before saving. Also insert the nickname index entry.

**Rationale**: Registration is the natural creation point — the user entity is being built anyway. No post-login fixup needed.

**Alternatives considered**:
- Post-login check: Adds conditional logic to every login. Wasteful — registration is the single creation moment.

---

## R5: Post-Login Redirect to Dashboard

**Context**: After successful login, users should land on `/dashboard` instead of `/`.

**Decision**: Update the Identity `Login.razor` page to default `ReturnUrl` to `"/dashboard"` when not specified. Update `RedirectToLogin` in Account shared components to use `/dashboard` as the post-login destination.

**Rationale**: Minimal change — only touches the existing scaffolded pages' default redirect target.

**Alternatives considered**:
- Global middleware redirect: Adds latency to every request. Landing page handles its own redirect.

---

## R6: SignalR Hub for Global Dashboard Refresh

**Context**: Need a SignalR hub for real-time dashboard state refresh. Clarification specifies global push — all connected dashboard clients receive updates on any game-state change.

**Decision**: Create an `[Authorize]` hub class. On any game mutation (create, join, cancel), broadcast `DashboardStateRefreshed` to **all connected clients** via `Clients.All`. Keep `JoinConflict` as a targeted per-player message via `Clients.User()`.

**Rationale**: Global broadcast is simpler than per-player filtering. With the shell app's expected scale (small number of concurrent players), broadcasting "refresh your dashboard" to all clients is proportional. Each client re-queries its own state and renders accordingly.

**Key patterns**:
```csharp
[Authorize]
public class BoxCarsHub : Hub { }

// In GameService, after any game mutation:
await hubContext.Clients.All.SendAsync("DashboardStateRefreshed", cancellationToken);

// For join conflicts (targeted):
await hubContext.Clients.User(playerId).SendAsync("JoinConflict", new { gameId, reason }, cancellationToken);
```

**Change from initial design**: Removed `JoinPlayerChannel`/`LeavePlayerChannel` methods — not needed for global broadcast. `DashboardStateRefreshed` goes to `Clients.All`. `JoinConflict` uses `Clients.User()` which maps to the Identity user ID via the built-in `IUserIdProvider`.

**NuGet**: Add `Microsoft.AspNetCore.SignalR.Client` (for Blazor component `HubConnectionBuilder`).

**Alternatives considered**:
- Per-player groups: More complex, not justified at current scale. Can optimize later if needed.

---

## R7: Landing Page Auth-Redirect Pattern

**Context**: The root URL (`/`) should show the landing page for unauthenticated visitors but auto-redirect authenticated users to `/dashboard`.

**Decision**: Use `[AllowAnonymous]` on `Home.razor` and check `AuthenticationState` cascading parameter to redirect authenticated users.

**Rationale**: Self-contained in the landing page component. No middleware needed.

**Key patterns**:
```razor
@page "/"
@attribute [AllowAnonymous]
@inject NavigationManager Navigation

@code {
    [CascadingParameter]
    private Task<AuthenticationState>? AuthState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (AuthState != null)
        {
            var state = await AuthState;
            if (state.User.Identity?.IsAuthenticated == true)
                Navigation.NavigateTo("/dashboard");
        }
    }
}
```

---

## R8: Game Data in Azure Table Storage

**Context**: Dashboard needs game availability data (join vs. create). All data must be in Azure Table Storage.

**Decision**: Store game records in `GamesTable` (mono table). PartitionKey = game status category, RowKey = game ID. Store game-player associations in `GamePlayersTable` (mono table). PartitionKey = gameId, RowKey = playerId. Track active game constraint via `PlayerActiveGameIndexTable`.

**Rationale**: Natural table storage patterns — partition by access pattern. Game queries are by status (active games) and by player participation.

**Key patterns**:
```csharp
public class GameEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "ACTIVE"; // ACTIVE, COMPLETED, CANCELLED
    public string RowKey { get; set; } = string.Empty;   // Game ID (GUID string)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CreatorId { get; set; } = string.Empty;
    public int MaxPlayers { get; set; } = 6;             // 2–6 configurable
    public int CurrentPlayerCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
}
```

**Status transitions**: When a game changes status, delete the old row and insert with new PartitionKey (table storage cannot update partition keys in place). Use ETag for optimistic concurrency.

**Alternatives considered**:
- Hetero table (games + players in one table): Complicates queries and violates constitution mono table naming convention.

---

## R9: Azurite Local Development Setup

**Context**: Azure Table Storage needs a local emulator for development.

**Decision**: Use Azurite (official Azure Storage emulator). Install via npm or Docker. Configure connection string to `UseDevelopmentStorage=true`.

**Rationale**: Azurite is the Microsoft-supported local emulator. `UseDevelopmentStorage=true` routes to `127.0.0.1:10002` (table service port).

**Key patterns**:
```json
{
  "AzureTableStorage": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

```csharp
builder.Services.AddSingleton(sp =>
{
    var connStr = builder.Configuration["AzureTableStorage:ConnectionString"];
    return new TableServiceClient(connStr);
});
```

---

## R10: Removing EF Core and SQL Server

**Context**: The existing project has EF Core + SQL Server packages, `ApplicationDbContext`, and migrations. All must be removed.

**Decision**: Remove all EF Core NuGet packages, delete `ApplicationDbContext.cs`, delete `Data/Migrations/` directory, and remove all EF Core service registrations from `Program.cs`. Replace with `Azure.Data.Tables` registration.

**Files to remove/modify**:
- Delete: `Data/ApplicationDbContext.cs`, `Data/Migrations/` (entire directory)
- Remove from `Boxcars.csproj`: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Tools`, `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore`
- Remove from `Program.cs`: `AddDbContext`, `AddDatabaseDeveloperPageExceptionFilter`, `UseMigrationsEndPoint`, `AddEntityFrameworkStores`
- Remove from `appsettings.json`: `ConnectionStrings` section
- Modify: `ApplicationUser.cs` — change from `IdentityUser` subclass to `ITableEntity` implementation

---

## R11: Microsoft Fluent UI Blazor Components

**Context**: The UI should use Microsoft Fluent UI Blazor component library for consistent design language. Replaces Bootstrap.

**Decision**: Use `Microsoft.FluentUI.AspNetCore.Components` v4.x (latest stable for .NET 8) and `Microsoft.FluentUI.AspNetCore.Components.Icons` for icons.

**Rationale**: Provides production-quality Blazor components following Microsoft's Fluent Design System. Well-suited for .NET 8 Blazor Server. Includes layout components (`FluentLayout`, `FluentHeader`, `FluentBodyContent`), profile menu (`FluentProfileMenu`), cards (`FluentCard`), buttons (`FluentButton`), text fields (`FluentTextField`), dialogs, toasts, and a complete icon set.

**Setup requirements**:

1. **NuGet packages**:
   ```xml
   <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" Version="4.*" />
   <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components.Icons" Version="4.*" />
   ```

2. **Program.cs**:
   ```csharp
   builder.Services.AddHttpClient();              // Required for Blazor Server
   builder.Services.AddFluentUIComponents();
   ```

3. **_Imports.razor**:
   ```razor
   @using Microsoft.FluentUI.AspNetCore.Components
   @using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons
   ```

4. **App.razor** (head section):
   ```html
   <link href="_content/Microsoft.FluentUI.AspNetCore.Components/css/reboot.css" rel="stylesheet" />
   ```

5. **MainLayout.razor** (providers at end of layout):
   ```razor
   <FluentToastProvider />
   <FluentDialogProvider />
   <FluentTooltipProvider />
   <FluentMessageBarProvider />
   ```

**Key layout pattern** — Top nav bar with brand left, profile menu right:
```razor
<FluentLayout>
    <FluentHeader>
        <FluentStack Orientation="Orientation.Horizontal"
                     HorizontalAlignment="HorizontalAlignment.SpaceBetween"
                     VerticalAlignment="VerticalAlignment.Center"
                     Width="100%">
            <span>BoxCars</span>
            <FluentProfileMenu FullName="@nickname"
                               EMail="@email"
                               TopCorner="true" />
        </FluentStack>
    </FluentHeader>
    <FluentBodyContent>
        @Body
    </FluentBodyContent>
</FluentLayout>
```

**Key UI components for this feature**:
- `FluentCard` — dashboard panels (stats, game action, profile)
- `FluentButton` — Create Game / Join Game actions
- `FluentTextField` — nickname and thumbnail URL inputs on settings page
- `FluentPersona` — avatar/nickname display
- `FluentProfileMenu` — top-right profile popover with Settings / Sign Out
- `FluentMessageBar` / `IMessageService` — conflict and error messages
- `FluentDesignTheme` — theme support (dark/light auto-persisted)

**Theming**: `<FluentDesignTheme StorageName="theme" />` in App.razor for auto-persisted light/dark preference.

**Blazor Server gotchas**:
- Must register `AddHttpClient()` before `AddFluentUIComponents()`
- Provider components need interactive render mode
- Set `TopCorner="true"` on `FluentProfileMenu` to avoid positioning issues

**CSS migration**: Bootstrap (`bootstrap.min.css`) removed from `wwwroot/`. Fluent UI's `reboot.css` replaces the reset. Body styles use Fluent CSS variables (`--neutral-foreground-rest`, `--neutral-fill-layer-rest`, etc.).

**Alternatives considered**:
- MudBlazor: Popular but not Microsoft-aligned. Fluent UI matches the project's .NET ecosystem.
- Bootstrap (already present): Generic, doesn't provide Blazor-native components.
