using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Boxcars.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /login/{scheme}?returnUrl=/foo  -> challenge external provider
        endpoints.MapGet("/login/{scheme}", (string scheme, string? returnUrl, HttpContext ctx) =>
        {
            var safeReturn = !string.IsNullOrWhiteSpace(returnUrl)
                             && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl!
                : "/";

            var properties = new AuthenticationProperties
            {
                RedirectUri = safeReturn
            };

            return Results.Challenge(properties, [scheme]);
        });

        // GET /logout  -> sign out and bounce home
        endpoints.MapGet("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        });

        // GET /user-photo/{name}  -> stream a stored profile photo from the private container
        endpoints.MapGet($"{UserPhotoStorage.PhotoPathPrefix}/{{name}}", async (string name, UserPhotoStorage photoStorage, HttpContext ctx) =>
        {
            var photo = await photoStorage.OpenAsync(name, ctx.RequestAborted);
            if (photo is null)
            {
                return Results.NotFound();
            }

            ctx.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.Stream(photo.Value.Content, photo.Value.ContentType);
        })
        .RequireAuthorization();

        return endpoints;
    }
}
