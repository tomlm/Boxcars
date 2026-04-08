using System.Security.Claims;
using Azure;
using Azure.Data.Tables;
using Boxcars.Data;
using Boxcars.Identity;
using Microsoft.AspNetCore.Authentication;

namespace Boxcars.Auth;

/// <summary>
/// Ensures a row exists in the Users table for an externally-authenticated user
/// (Google, Microsoft, etc.). Creates the row on first sign-in and rewrites the
/// principal so that downstream code sees a stable NameIdentifier == user RowKey.
/// </summary>
public sealed class ExternalLoginProvisioner
{
    private readonly TableClient _usersTable;

    public ExternalLoginProvisioner(TableServiceClient tableServiceClient)
    {
        _usersTable = tableServiceClient.GetTableClient(TableNames.UsersTable);
    }

    public const string ExternalPictureClaimType = "urn:boxcars:external_picture";

    public Task EnsureUserAsync(TicketReceivedContext context, CancellationToken cancellationToken)
        => EnsureUserAsync(context, externalThumbnailUrl: null, cancellationToken);

    public async Task EnsureUserAsync(TicketReceivedContext context, string? externalThumbnailUrl, CancellationToken cancellationToken)
    {
        var principal = context.Principal;
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(externalThumbnailUrl))
        {
            externalThumbnailUrl = principal.FindFirstValue(ExternalPictureClaimType)
                ?? principal.FindFirstValue("picture");
        }

        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email")
            ?? principal.FindFirstValue("preferred_username");
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var rowKey = email.Trim().ToLowerInvariant();
        var displayName = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? email;

        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? string.Empty;
        var providerName = context.Scheme.Name;

        ApplicationUser? user = null;
        try
        {
            var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", rowKey, cancellationToken: cancellationToken);
            user = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        if (user is null)
        {
            var nicknameSeed = email.Split('@')[0];
            var now = DateTimeOffset.UtcNow;
            user = new ApplicationUser
            {
                PartitionKey = "USER",
                RowKey = rowKey,
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Name = displayName,
                Nickname = nicknameSeed,
                NormalizedNickname = nicknameSeed.ToUpperInvariant(),
                ExternalLoginProvider = providerName,
                ExternalLoginKey = providerKey,
                ThumbnailUrl = externalThumbnailUrl ?? string.Empty,
                CreatedByUserId = rowKey,
                CreatedUtc = now,
                ModifiedByUserId = rowKey,
                ModifiedUtc = now
            };

            try
            {
                await _usersTable.AddEntityAsync(user, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                // Concurrent first-login race — fetch the winning row.
                var response = await _usersTable.GetEntityAsync<ApplicationUser>("USER", rowKey, cancellationToken: cancellationToken);
                user = response.Value;
            }
        }
        else if (!string.Equals(user.ExternalLoginProvider, providerName, StringComparison.Ordinal)
                 || !string.Equals(user.ExternalLoginKey, providerKey, StringComparison.Ordinal))
        {
            user.ExternalLoginProvider = providerName;
            user.ExternalLoginKey = providerKey;
            user.ModifiedUtc = DateTimeOffset.UtcNow;
            try
            {
                await _usersTable.UpdateEntityAsync(user, user.ETag, TableUpdateMode.Merge, cancellationToken);
            }
            catch (RequestFailedException)
            {
                // Non-fatal — provisioning is best-effort.
            }
        }

        // Rewrite NameIdentifier so the rest of the app can use principal.NameIdentifier
        // as the canonical user id (lowercase email / Users-table RowKey).
        var existingNameId = identity.FindFirst(ClaimTypes.NameIdentifier);
        if (existingNameId is not null)
        {
            identity.RemoveClaim(existingNameId);
        }
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.RowKey));

        if (identity.FindFirst(ClaimTypes.Email) is null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        }
    }
}
