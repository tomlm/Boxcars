using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

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

        // POST /login/email (form: email, returnUrl) -> sign in as an email-only player; see EmailLoginService
        endpoints.MapPost("/login/email", async ([FromForm] string? email, [FromForm] string? returnUrl, EmailLoginService emailLogin, HttpContext ctx) =>
        {
            var safeReturn = !string.IsNullOrWhiteSpace(returnUrl)
                             && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl!
                : "/";

            var result = await emailLogin.ResolveAsync(email, ctx.RequestAborted);
            if (result.User is not { } user)
            {
                return Results.Redirect($"/login?returnUrl={Uri.EscapeDataString(safeReturn)}&error={Uri.EscapeDataString(result.Error ?? "Unable to sign in.")}");
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.RowKey),
                    new Claim(ClaimTypes.Email, user.Email),
                    new Claim(ClaimTypes.Name, EmailLoginService.DisplayName(user))
                ],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
            return Results.Redirect(safeReturn);
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
