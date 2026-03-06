using Azure.Data.Tables;
using Boxcars.Components;
using Boxcars.Components.Account;
using Boxcars.Data;
using Boxcars.GameEngine;
using Boxcars.Hubs;
using Boxcars.Identity;
using Boxcars.Services;
using Boxcars.Services.Maps;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.FluentUI.AspNetCore.Components;

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

        // Fluent UI
        builder.Services.AddHttpClient();
        builder.Services.AddFluentUIComponents();

        // Identity (custom table storage stores, no EF Core)
        builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
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
        builder.Services.AddScoped<PlayerProfileService>();
        builder.Services.AddScoped<GameService>();
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
            (Email: "paul@beatles.com", Name: "Paul McCartney", Nickname: "Paul"),
            (Email: "ringo@beatles.com", Name: "Ringo Starr", Nickname: "Ringo"),
            (Email: "george@beatles.com", Name: "George Harrison", Nickname: "George"),
            (Email: "john@beatles.com", Name: "John Lennon", Nickname: "John")
        };

        foreach (var beatle in beatlesUsers)
        {
            var userId = beatle.Email.ToLowerInvariant();
            var existing = await usersTable.GetEntityIfExistsAsync<ApplicationUser>("USER", userId);
            if (existing.HasValue)
            {
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
