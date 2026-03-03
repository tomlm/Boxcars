using Azure.Data.Tables;
using Boxcars.Components;
using Boxcars.Components.Account;
using Boxcars.Data;
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

        // Application services
        builder.Services.AddScoped<PlayerProfileService>();
        builder.Services.AddScoped<GameService>();
        builder.Services.AddScoped<IMapParserService, RbpMapParserService>();
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
            TableNames.UserEmailIndexTable,
            TableNames.UserNameIndexTable,
            TableNames.NicknameIndexTable,
            TableNames.GamesTable,
            TableNames.GamePlayersTable,
            TableNames.PlayerActiveGameIndexTable
        };
        foreach (var tableName in tableNames)
        {
            await tableService.CreateTableIfNotExistsAsync(tableName);
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
        app.MapHub<BoxCarsHub>("/hubs/boxcars");

        // Add additional endpoints required by the Identity /Account Razor components.
        app.MapAdditionalIdentityEndpoints();

        app.Run();
    }
}
