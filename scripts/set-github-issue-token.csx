#r "nuget: CShell, 3.2.0"
global using static CShellNet.Globals;
using CShellNet;
using System.Net.Http;
using System.Net.Http.Headers;

// Turns on automatic GitHub issues for route-planner failures (RouteFailureIssueReporter).
//
//   dotnet script scripts/set-github-issue-token.csx
//
// GitHub only creates fine-grained tokens on its website, so this script:
//   1. opens the token page (pre-filled where GitHub supports it),
//   2. reads the token you paste (hidden),
//   3. checks it can read tomlm/Boxcars issues,
//   4. stores it as the Azure app setting GitHub__IssueToken with `az` (signed in to the subscription).
// The token is never printed. Changing an app setting restarts the app.

const string Owner = "tomlm";
const string Repository = "Boxcars";
const string WebApp = "boxcars";
const string ResourceGroup = "MiscSites";
const string SettingName = "GitHub__IssueToken";

// Commands carry the token, so never echo them.
Echo = false;

var tokenPage = "https://github.com/settings/personal-access-tokens/new"
    + "?name=" + Uri.EscapeDataString("Boxcars route failure issues")
    + "&description=" + Uri.EscapeDataString("Lets the Boxcars app file issues when the route planner fails.")
    + "&target_name=" + Owner
    + "&expires_in=366"
    + "&issues=write";

print("Opening the GitHub token page. Set:");
print($"  Resource owner:      {Owner}");
print($"  Repository access:   Only select repositories -> {Owner}/{Repository}");
print("  Permissions:         Repository -> Issues -> Read and write");
print("");
// Opens the URL in the default browser.
Start(tokenPage);

var token = AskSecret("Paste the token:").Trim();
if (token.Length == 0)
{
    error("No token entered.");
    return 1;
}

using (var client = new HttpClient())
{
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Boxcars-setup");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

    using var response = await client.GetAsync($"https://api.github.com/repos/{Owner}/{Repository}/issues?per_page=1");
    if (!response.IsSuccessStatusCode)
    {
        error($"The token can't read {Owner}/{Repository} issues ({(int)response.StatusCode} {response.ReasonPhrase}). Check its repository access and Issues permission.");
        return 1;
    }
}

// Issue *write* access can't be checked without filing an issue; it was chosen on the token page above.
print($"Token can read {Owner}/{Repository} issues. Setting {SettingName} on {WebApp}...");

var result = await Cmd($"az webapp config appsettings set -n {WebApp} -g {ResourceGroup} --settings {SettingName}={token} --output none").Execute();
if (!result.Success)
{
    error($"az failed (exit code {result.ExitCode}): {result.StandardError.Trim()}");
    error($"Run 'az login' and check you can manage {WebApp}.");
    return result.ExitCode;
}

print($"Done. {WebApp} restarts with {SettingName} set; route-planner failures will now file issues in {Owner}/{Repository}.");
return 0;
