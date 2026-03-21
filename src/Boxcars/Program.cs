using Azure.Data.Tables;
using Boxcars.Components;
using Boxcars.Components.Account;
using Boxcars.Data;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Identity;
using Boxcars.Services;
using Boxcars.Services.Maps;
using Microsoft.AspNetCore.Components.Server.Circuits;
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

        builder.Services.AddOptions<BotOptions>()
            .Configure(options =>
            {
                builder.Configuration.GetSection(BotOptions.SectionName).Bind(options);

                if (string.IsNullOrWhiteSpace(options.OpenAIKey))
                {
                    options.OpenAIKey = builder.Configuration[BotOptions.LegacyApiKeySettingName] ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(options.ServerActorUserId))
                {
                    options.ServerActorUserId = BotOptions.DefaultServerActorUserId;
                }

                if (string.IsNullOrWhiteSpace(options.ServerActorDisplayName))
                {
                    options.ServerActorDisplayName = BotOptions.DefaultServerActorDisplayName;
                }
            })
            .Validate(static options => options.DecisionTimeoutSeconds > 0, "Bots:DecisionTimeoutSeconds must be greater than zero.")
            .Validate(static options => string.IsNullOrWhiteSpace(options.OpenAIModel) is false, "Bots:OpenAIModel is required.")
            .Validate(static options => string.IsNullOrWhiteSpace(options.ServerActorUserId) is false, "Bots:ServerActorUserId is required.")
            .Validate(static options => string.IsNullOrWhiteSpace(options.ServerActorDisplayName) is false, "Bots:ServerActorDisplayName is required.")
            .ValidateOnStart();

        // UI component libraries
        builder.Services.AddHttpClient();
        builder.Services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.NewestOnTop = false;
            config.SnackbarConfiguration.MaxDisplayedSnackbars = 4;
            config.SnackbarConfiguration.PreventDuplicates = true;
            config.SnackbarConfiguration.VisibleStateDuration = 8000;
        });
        builder.Services.AddMudMarkdownServices();

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
        builder.Services.AddScoped<GameCircuitPresenceTracker>();
        builder.Services.AddScoped<CircuitHandler, GamePresenceCircuitHandler>();
        builder.Services.AddSingleton<GamePresenceService>(serviceProvider =>
            new GamePresenceService(serviceProvider.GetRequiredService<TableServiceClient>()));
        builder.Services.AddSingleton<UserDirectoryService>();
        builder.Services.AddSingleton<BotDecisionPromptBuilder>();
        builder.Services.AddSingleton<OpenAiBotClient>();
        builder.Services.AddSingleton<BotTurnService>();
        builder.Services.AddScoped<GameBoardStateMapper>();
        builder.Services.AddScoped<GameBoardAdviceService>();
        builder.Services.AddSingleton<NetworkCoverageService>();
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
        var bots = new[]
        {
            (Email: "connecto@boxcars.net", Name: "Connecto", Nickname: "Connecto", PreferredColor: "purple", Strategy: "This bot values connectivity and access, and tries to build an optimal network to get to the most likely cities." ),
            (Email: "balenco@boxcars.net", Name: "Balenco", Nickname: "Balenco", PreferredColor: "orange", Strategy: "This bot strategically hybrid balance of access, connectivity, regional access and opportunities to monopolize cities." ),
            (Email: "pooper@boxcars.net", Name: "Pooper", Nickname: "Pooper", PreferredColor: "yellow", Strategy: "This bot likes to purchase a Superchief before any RRs, and then uses that engine to race to make money to buy a balanced mixture of access and monopoly." ),
            (Email: "rat@boxcars.net", Name: "Rat Bastard", Nickname: "Rat Bastard", PreferredColor: "darkred", Strategy: "This bot loves to create monopolies for cities and regions and using the proceeds from that to buy access and larger RRs" ),
            (Email: "cheapo@boxcars.net", Name: "El Cheapo", Nickname: "El Cheapo", PreferredColor: "darkred", Strategy: "This bot loves to create monopolies for cities and regions and using the proceeds from that to buy access and larger RRs" )
        };

        foreach (var bot in bots)
        {
            var userId = bot.Email.ToLowerInvariant();
            var existing = await usersTable.GetEntityIfExistsAsync<ApplicationUser>("USER", userId);
            if (existing.HasValue && existing.Value is { } existingUser)
            {
                var normalizedPreferredColor = PlayerColorOptions.NormalizeOrDefault(bot.PreferredColor);
                var desiredStrategy = bot.Strategy;
                if (!string.Equals(existingUser.PreferredColor, normalizedPreferredColor, StringComparison.OrdinalIgnoreCase)
                    || !existingUser.IsBot
                    || !string.Equals(existingUser.StrategyText, desiredStrategy, StringComparison.Ordinal))
                {
                    existingUser.PreferredColor = normalizedPreferredColor;
                    existingUser.IsBot = true;
                    existingUser.StrategyText = existingUser.StrategyText ?? desiredStrategy ?? PlayerProfileService.DefaultStrategyText;
                    existingUser.ModifiedUtc = DateTimeOffset.UtcNow;
                    await usersTable.UpdateEntityAsync(existingUser, existingUser.ETag, TableUpdateMode.Replace);
                }

                continue;
            }

            var now = DateTimeOffset.UtcNow;

            await usersTable.AddEntityAsync(new ApplicationUser
            {
                PartitionKey = "USER",
                RowKey = userId,
                Email = bot.Email,
                NormalizedEmail = bot.Email.ToUpperInvariant(),
                UserName = bot.Email,
                NormalizedUserName = bot.Email.ToUpperInvariant(),
                Name = bot.Name,
                Nickname = bot.Nickname,
                NormalizedNickname = bot.Nickname.ToUpperInvariant(),
                PreferredColor = PlayerColorOptions.NormalizeOrDefault(bot.PreferredColor),
                StrategyText = bot.Strategy ?? PlayerProfileService.DefaultStrategyText,
                IsBot = true,
                CreatedByUserId = "system",
                CreatedUtc = now,
                ModifiedByUserId = "system",
                ModifiedUtc = now,
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
