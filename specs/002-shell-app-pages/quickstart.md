# Quickstart: BoxCars Shell App Pages (Feature 002)

**Purpose**: Get the development environment running for the BoxCars shell application pages feature.

## Prerequisites

- .NET 8 SDK installed
- **Azurite** (Azure Storage Emulator) — choose one:
  - **npm**: `npm install -g azurite`
  - **Docker**: `docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite`
  - **VS Code**: Install the "Azurite" extension, then start via command palette
- No SQL Server required
- No Entity Framework migrations required

## Setup Steps

### 1. Start Azurite

Start Azurite in a separate terminal before running the application.

```bash
# Option A: npm (all services)
azurite --silent --location .azurite --debug .azurite/debug.log

# Option B: npm (table service only)
azurite-table --silent

# Option C: Docker
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

Table Storage endpoint: `http://127.0.0.1:10002`

### 2. Verify Configuration

`appsettings.Development.json` should contain:

```json
{
  "AzureTableStorage": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

### 3. Run the Application

```bash
cd src/Boxcars
dotnet run
```

### 4. Table Auto-Creation

Tables are automatically created on first application startup if they don't exist:

| Table Name | Purpose |
|---|---|
| `UsersTable` | Identity user accounts + player profile data |
| `UserEmailIndexTable` | Email → UserId lookup |
| `UserNameIndexTable` | Username → UserId lookup |
| `NicknameIndexTable` | Nickname uniqueness enforcement |
| `GamesTable` | Game sessions (partitioned by status) |
| `GamePlayersTable` | Player membership per game |
| `PlayerActiveGameIndexTable` | Active game constraint per player |

### 5. Verify

- Navigate to the URL shown in console output (e.g., `https://localhost:5001`)
- You should see the landing page with Fluent UI styling and a "Play Now" button
- Click "Play Now" → redirects to Identity login/register
- Register a new account → redirected to dashboard
- Dashboard shows nickname, avatar area, stats placeholders, and game creation UI
- Top nav bar shows BoxCars brand (left) and profile menu (right)

## NuGet Packages

After this feature, `Boxcars.csproj` should contain:

```xml
<ItemGroup>
  <PackageReference Include="Azure.Data.Tables" Version="*" />
  <PackageReference Include="Microsoft.AspNetCore.Authentication.MicrosoftAccount" Version="8.*" />
  <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.*" />
  <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components" Version="4.*" />
  <PackageReference Include="Microsoft.FluentUI.AspNetCore.Components.Icons" Version="4.*" />
</ItemGroup>
```

Packages **removed** (EF Core):
- `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore`
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.SqlServer`
- `Microsoft.EntityFrameworkCore.Tools`

## Troubleshooting

- **"Unable to connect to Azure Table Storage"**: Ensure Azurite is running. Check that `UseDevelopmentStorage=true` is set in appsettings.
- **Tables not created**: Check application startup logs for table creation errors.
- **Old EF/SQL errors**: Ensure all EF Core packages are removed from `Boxcars.csproj` and `ApplicationDbContext.cs` is deleted. No `ConnectionStrings` section should exist in appsettings.
- **Fluent UI components not rendering**: Verify `AddFluentUIComponents()` is called in `Program.cs` after `AddHttpClient()`. Check that `_Imports.razor` has the `@using Microsoft.FluentUI.AspNetCore.Components` directive.
- **Missing icons**: Verify `Microsoft.FluentUI.AspNetCore.Components.Icons` NuGet package is installed and `@using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons` is in `_Imports.razor`.

## Data Reset

To wipe all local data, stop Azurite and delete the `.azurite` directory (or the Docker volume), then restart.
