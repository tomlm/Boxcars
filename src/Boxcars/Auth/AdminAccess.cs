using System.Security.Claims;

namespace Boxcars.Auth;

/// <summary>
/// Who runs this site: the users listed in the <c>Admin:UserIds</c> setting (emails, separated by commas or
/// semicolons, or a list). Kept in app settings and user-secrets, never in the repo.
/// </summary>
public sealed class AdminAccess
{
    /// <summary>The authorization policy that admits only admins.</summary>
    public const string PolicyName = "Admin";

    private readonly HashSet<string> _adminUserIds;

    public AdminAccess(IConfiguration configuration)
    {
        var section = configuration.GetSection("Admin:UserIds");
        var values = section.GetChildren().Select(child => child.Value).Append(section.Value);
        _adminUserIds = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(userId => userId.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    public bool IsAdmin(string? userId)
    {
        return !string.IsNullOrWhiteSpace(userId) && _adminUserIds.Contains(userId.Trim().ToLowerInvariant());
    }

    public bool IsAdmin(ClaimsPrincipal? user)
    {
        return user?.Identity?.IsAuthenticated == true && IsAdmin(user.FindFirstValue(ClaimTypes.NameIdentifier));
    }
}
