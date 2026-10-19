#r "nuget: CShell, 3.2.0"
#r "nuget: Azure.Data.Tables, 12.11.0"
#r "nuget: Azure.Identity, 1.13.2"
global using static CShellNet.Globals;
using CShellNet;
using System.Net.Http;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Identity;

// Copies a game from the live site into local storage (Azurite), to play it locally.
//
//   dotnet script scripts/clone-game.csx <game id or game URL>
//
// Copies the game's whole partition of GamesTable (the game, every event and its seats' state) and the UsersTable
// rows of its creator and seated players, so you can sign in locally as yourself and take your seat. Rows that
// already exist locally are replaced. The live game is only read, never changed.
//
// Needs `az` signed in to the subscription (to read the site's storage setting) and Azurite running locally. The
// storage connection string is never printed.

const string WebApp = "boxcars";
const string ResourceGroup = "MiscSites";
const string SettingName = "AzureTableStorage__ConnectionString";
const string LocalConnectionString = "UseDevelopmentStorage=true";
const string GamesTable = "GamesTable";
const string UsersTable = "UsersTable";

// Commands carry the connection string, so never echo them.
Echo = false;

if (Args.Count != 1 || string.IsNullOrWhiteSpace(Args[0]))
{
    error("Usage: dotnet script scripts/clone-game.csx <game id or game URL>");
    return 1;
}

var gameId = Args[0].Trim().TrimEnd('/').Split('/')[^1];

// The output holds the app's settings, so it's only parsed, never shown. (A --query filter would need quoting that
// doesn't survive the trip to az on Windows.)
var settings = await Cmd($"az webapp config appsettings list -n {WebApp} -g {ResourceGroup} -o json").Execute();
if (!settings.Success)
{
    error($"Couldn't read {WebApp}'s settings (exit code {settings.ExitCode}): {settings.StandardError.Trim()}");
    error($"Run 'az login' and check you can manage {WebApp}.");
    return 1;
}

string remoteSetting = null;
using (var settingsJson = JsonDocument.Parse(settings.StandardOutput))
{
    foreach (var appSetting in settingsJson.RootElement.EnumerateArray())
    {
        if (appSetting.GetProperty("name").GetString() == SettingName)
        {
            remoteSetting = appSetting.GetProperty("value").GetString()?.Trim();
        }
    }
}

if (string.IsNullOrWhiteSpace(remoteSetting))
{
    error($"{WebApp} has no {SettingName} setting.");
    return 1;
}
// The setting is either a connection string or, with a managed identity, the storage endpoint.
var remote = remoteSetting.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    ? new TableServiceClient(new Uri(remoteSetting), new DefaultAzureCredential())
    : new TableServiceClient(remoteSetting);
var local = new TableServiceClient(LocalConnectionString);

var remoteGames = remote.GetTableClient(GamesTable);
var remoteUsers = remote.GetTableClient(UsersTable);
var localGames = local.GetTableClient(GamesTable);
var localUsers = local.GetTableClient(UsersTable);
try
{
    await localGames.CreateIfNotExistsAsync();
    await localUsers.CreateIfNotExistsAsync();
}
catch (Exception exception) when (exception is RequestFailedException or AggregateException or HttpRequestException)
{
    error($"Couldn't reach local storage ({exception.Message}). Is Azurite running?");
    return 1;
}

TableEntity game;
try
{
    game = (await remoteGames.GetEntityAsync<TableEntity>(gameId, "GAME")).Value;
}
catch (RequestFailedException exception) when (exception.Status == 404)
{
    error($"Game {gameId} wasn't found on {WebApp}.");
    return 1;
}

print($"Copying \"{game.GetString("Name")}\" ({gameId})...");

var rows = 0;
await foreach (var entity in remoteGames.QueryAsync<TableEntity>(entity => entity.PartitionKey == gameId))
{
    await localGames.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    rows++;
}

print($"  {rows} game rows (game, events, seats).");

var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
if (game.GetString("CreatorId") is { Length: > 0 } creatorId)
{
    userIds.Add(creatorId);
}

using (var seats = JsonDocument.Parse(game.GetString("SeatsJson") ?? "[]"))
{
    foreach (var seat in seats.RootElement.EnumerateArray())
    {
        if (seat.TryGetProperty("PlayerUserId", out var userId) && userId.GetString() is { Length: > 0 } id)
        {
            userIds.Add(id);
        }
    }
}

var users = 0;
foreach (var userId in userIds)
{
    try
    {
        var user = (await remoteUsers.GetEntityAsync<TableEntity>("USER", userId)).Value;
        await localUsers.UpsertEntityAsync(user, TableUpdateMode.Replace);
        users++;
    }
    catch (RequestFailedException exception) when (exception.Status == 404)
    {
        // Bots and open seats have no user row.
    }
}

print($"  {users} users.");
print($"Done. Run the app locally and open /game/{gameId}.");
return 0;
