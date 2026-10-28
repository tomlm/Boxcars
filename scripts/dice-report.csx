#r "nuget: CShell, 3.2.0"
#r "nuget: Azure.Data.Tables, 12.11.0"
#r "nuget: Azure.Identity, 1.13.2"
global using static CShellNet.Globals;
using CShellNet;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Identity;

// Lists every dice roll in a live game and how the faces came up, to check the dice look random.
//
//   dotnet script scripts/dice-report.csx <game id or game URL>
//
// Reads the game's events from the live site's storage; nothing is changed. Each roll is taken from the dice in
// the saved game state, so a roll is listed once even when several events share it. Needs `az` signed in to the
// subscription (to read the site's storage setting). The storage connection string is never printed.

const string WebApp = "boxcars";
const string ResourceGroup = "MiscSites";
const string SettingName = "AzureTableStorage__ConnectionString";
const string GamesTable = "GamesTable";

// Commands carry the connection string, so never echo them.
Echo = false;

if (Args.Count != 1 || string.IsNullOrWhiteSpace(Args[0]))
{
    error("Usage: dotnet script scripts/dice-report.csx <game id or game URL>");
    return 1;
}

var gameId = Args[0].Trim().TrimEnd('/').Split('/')[^1];

// The output holds the app's settings, so it's only parsed, never shown.
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
var games = remote.GetTableClient(GamesTable);

// Events are stored newest first; put them back in the order they happened.
var events = new List<(DateTimeOffset OccurredUtc, string RowKey, string State)>();
await foreach (var entity in games.QueryAsync<TableEntity>(
    entity => entity.PartitionKey == gameId && entity.RowKey != "GAME",
    select: ["RowKey", "OccurredUtc", "SerializedGameState"]))
{
    if (entity.GetString("SerializedGameState") is { Length: > 0 } state)
    {
        events.Add((entity.GetDateTimeOffset("OccurredUtc") ?? DateTimeOffset.MinValue, entity.RowKey, state));
    }
}

if (events.Count == 0)
{
    error($"Game {gameId} has no events on {WebApp}.");
    return 1;
}

events.Sort((left, right) => left.OccurredUtc != right.OccurredUtc
    ? left.OccurredUtc.CompareTo(right.OccurredUtc)
    : string.CompareOrdinal(right.RowKey, left.RowKey));

// A roll is new when the turn or the dice change from the previous event's.
var rolls = new List<(int Turn, string Player, int[] White, int? Red)>();
string previousKey = null;
foreach (var gameEvent in events)
{
    using var stateJson = JsonDocument.Parse(gameEvent.State);
    var root = stateJson.RootElement;
    if (!root.TryGetProperty("Turn", out var turn)
        || !turn.TryGetProperty("DiceResult", out var dice)
        || dice.ValueKind != JsonValueKind.Object)
    {
        previousKey = null;
        continue;
    }

    var turnNumber = root.GetProperty("TurnNumber").GetInt32();
    var white = dice.GetProperty("WhiteDice").EnumerateArray().Select(die => die.GetInt32()).ToArray();
    int? red = dice.TryGetProperty("RedDie", out var redDie) && redDie.ValueKind == JsonValueKind.Number ? redDie.GetInt32() : null;
    var key = $"{turnNumber}|{string.Join(",", white)}|{red}";
    if (key == previousKey)
    {
        continue;
    }

    previousKey = key;
    var playerIndex = root.GetProperty("ActivePlayerIndex").GetInt32();
    var player = root.GetProperty("Players")[playerIndex].GetProperty("Name").GetString();
    rolls.Add((turnNumber, player, white, red));
}

string Describe((int Turn, string Player, int[] White, int? Red) roll) => roll.White.All(die => die == 0)
    ? $"bonus {roll.Red}"
    : roll.Red is int red ? $"{roll.White[0]}+{roll.White[1]}+({red})" : $"{roll.White[0]}+{roll.White[1]}";

print($"{rolls.Count} rolls in {events.Count} events.");
print("");
foreach (var roll in rolls)
{
    print($"  turn {roll.Turn,4}  {roll.Player,-12} {Describe(roll)}");
}

var faces = rolls
    .SelectMany(roll => roll.White.Where(die => die > 0).Concat(roll.Red is int red ? [red] : []))
    .ToList();
print("");
print($"Faces ({faces.Count} dice; {faces.Count / 6.0:F1} expected each):");
for (var face = 1; face <= 6; face++)
{
    print($"  {face}: {faces.Count(value => value == face)}");
}

var expected = faces.Count / 6.0;
var chiSquare = Enumerable.Range(1, 6).Sum(face => Math.Pow(faces.Count(value => value == face) - expected, 2) / expected);
// 5 degrees of freedom: above 11.07 happens by chance 5% of the time, above 15.09 1% of the time.
print($"  chi-square {chiSquare:F2} (5% cutoff 11.07, 1% cutoff 15.09)");

var whiteRolls = rolls.Where(roll => roll.White.Length >= 2 && roll.White[0] > 0).ToList();
print("");
print($"Two white dice: {whiteRolls.Count} rolls");
print($"  doubles {whiteRolls.Count(roll => roll.White[0] == roll.White[1])} ({whiteRolls.Count / 6.0:F1} expected)");
print($"  6+6     {whiteRolls.Count(roll => roll.White[0] == 6 && roll.White[1] == 6)} ({whiteRolls.Count / 36.0:F1} expected)");
print($"  1+1     {whiteRolls.Count(roll => roll.White[0] == 1 && roll.White[1] == 1)} ({whiteRolls.Count / 36.0:F1} expected)");

// The longest run of identical rolls in a row, across all players.
var longestRun = 1;
var longestRunEnd = 0;
var run = 1;
for (var index = 1; index < rolls.Count; index++)
{
    run = Describe(rolls[index]) == Describe(rolls[index - 1]) ? run + 1 : 1;
    if (run > longestRun)
    {
        longestRun = run;
        longestRunEnd = index;
    }
}

print("");
print(rolls.Count > 0
    ? $"Longest run of the same roll: {longestRun} x {Describe(rolls[longestRunEnd])}, ending turn {rolls[longestRunEnd].Turn}."
    : "No rolls.");
return 0;
