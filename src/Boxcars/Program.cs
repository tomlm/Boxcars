using Azure.Data.Tables;
using Boxcars.Components;
using Boxcars.Components.Account;
using Boxcars.Data;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Identity;
using Boxcars.Services;
using Boxcars.Services.Maps;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using MudBlazor;
using MudBlazor.Services;

namespace Boxcars;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IdentityUserAccessor>();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        // Azure Table Storage
        var tableStorageConnectionString = builder.Configuration["AzureTableStorage:ConnectionString"]
            ?? throw new InvalidOperationException("AzureTableStorage:ConnectionString not found.");
        builder.Services.AddSingleton(new TableServiceClient(tableStorageConnectionString));

        // UI component libraries
        builder.Services.AddHttpClient();
        builder.Services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = $"{Defaults.Classes.Position.BottomLeft} boxcars-snackbar-zone";
            config.SnackbarConfiguration.NewestOnTop = false;
            config.SnackbarConfiguration.MaxDisplayedSnackbars = 4;
            config.SnackbarConfiguration.PreventDuplicates = true;
            config.SnackbarConfiguration.VisibleStateDuration = 8000;
        });

        // Identity (custom table storage stores, no EF Core)
        builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.AddScoped<IUserStore<ApplicationUser>, TableStorageUserStore>();

        builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

        // SignalR
        builder.Services.AddSignalR();

        // Shared authoritative game engine
        builder.Services.AddSingleton<GameEngineService>();
        builder.Services.AddSingleton<IGameEngine>(serviceProvider => serviceProvider.GetRequiredService<GameEngineService>());
        builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GameEngineService>());
        builder.Services.AddHostedService<GameStateBroadcastService>();

        // Application services
        builder.Services.AddOptions<PurchaseRulesOptions>()
            .Bind(builder.Configuration.GetSection(PurchaseRulesOptions.SectionName))
            .Validate(static options => options.SuperchiefPrice > 0, "PurchaseRules:SuperchiefPrice must be greater than zero.")
            .ValidateOnStart();
        builder.Services.AddScoped<PlayerProfileService>();
        builder.Services.AddScoped<GameService>();
        builder.Services.AddSingleton<GamePresenceService>();
        builder.Services.AddScoped<GameBoardStateMapper>();
        builder.Services.AddScoped<NetworkCoverageService>();
        builder.Services.AddScoped<MapAnalysisService>();
        builder.Services.AddScoped<PurchaseRecommendationService>();
        builder.Services.AddScoped<MapBackgroundResolver>();
        builder.Services.AddScoped<BoardProjectionService>();
        builder.Services.AddScoped<BoardViewportService>();
        builder.Services.AddScoped<MapRouteService>();

        var app = builder.Build();

        // Auto-create tables on startup
        var tableService = app.Services.GetRequiredService<TableServiceClient>();
        var tableNames = new[]
        {
            TableNames.UsersTable,
            TableNames.GamesTable
        };
        foreach (var tableName in tableNames)
        {
            await tableService.CreateTableIfNotExistsAsync(tableName);
        }

        var usersTable = tableService.GetTableClient(TableNames.UsersTable);
        var beatlesUsers = new[]
        {
            (Email: "paul@beatles.com", Name: "Paul McCartney", Nickname: "Paul", PreferredColor: "purple"),
            (Email: "ringo@beatles.com", Name: "Ringo Starr", Nickname: "Ringo", PreferredColor: "red"),
            (Email: "george@beatles.com", Name: "George Harrison", Nickname: "George", PreferredColor: "green"),
            (Email: "john@beatles.com", Name: "John Lennon", Nickname: "John", PreferredColor: "orange")
        };

        foreach (var beatle in beatlesUsers)
        {
            var userId = beatle.Email.ToLowerInvariant();
            var existing = await usersTable.GetEntityIfExistsAsync<ApplicationUser>("USER", userId);
            if (existing.HasValue && existing.Value is { } existingUser)
            {
                var normalizedPreferredColor = PlayerColorOptions.NormalizeOrDefault(beatle.PreferredColor);
                if (!string.Equals(existingUser.PreferredColor, normalizedPreferredColor, StringComparison.OrdinalIgnoreCase))
                {
                    existingUser.PreferredColor = normalizedPreferredColor;
                    await usersTable.UpdateEntityAsync(existingUser, existingUser.ETag, TableUpdateMode.Replace);
                }

                continue;
            }

            await usersTable.AddEntityAsync(new ApplicationUser
            {
                PartitionKey = "USER",
                RowKey = userId,
                Email = beatle.Email,
                NormalizedEmail = beatle.Email.ToUpperInvariant(),
                UserName = beatle.Email,
                NormalizedUserName = beatle.Email.ToUpperInvariant(),
                Name = beatle.Name,
                Nickname = beatle.Nickname,
                NormalizedNickname = beatle.Nickname.ToUpperInvariant(),
                PreferredColor = PlayerColorOptions.NormalizeOrDefault(beatle.PreferredColor),
                ThumbnailUrl = "https://via.placeholder.com/150?text=Player",
                SecurityStamp = Guid.NewGuid().ToString(),
                EmailConfirmed = true
            });
        }

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // SignalR hub
        app.MapHub<DashboardHub>("/hubs/dashboard");
        app.MapHub<GameHub>("/hubs/game");

        // Add additional endpoints required by the Identity /Account Razor components.
        app.MapAdditionalIdentityEndpoints();

        app.Run();
    }
}
