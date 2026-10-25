using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Boxcars.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /login/{scheme}?returnUrl=/foo&player=key  -> challenge external provider. With a player picked on the
        // sign-in page, the provider is asked to suggest that person's email.
        endpoints.MapGet("/login/{scheme}", async (string scheme, string? returnUrl, string? player, EmailLoginService emailLogin, HttpContext ctx) =>
        {
            var safeReturn = !string.IsNullOrWhiteSpace(returnUrl)
                             && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl!
                : "/";

            var properties = new AuthenticationProperties
            {
                RedirectUri = safeReturn
            };

            if (await emailLogin.FindByPlayerKeyAsync(player, ctx.RequestAborted) is { } person
                && !string.IsNullOrWhiteSpace(person.Email))
            {
                // Google and Microsoft both read login_hint.
                properties.SetParameter("login_hint", person.Email);
            }

            return Results.Challenge(properties, [scheme]);
        });

        // POST /login/email (form: email or player, returnUrl) -> sign in as an email-only player, picked by name on the
        // sign-in page (player) or typed (email); see EmailLoginService
        endpoints.MapPost("/login/email", async ([FromForm] string? email, [FromForm] string? player, [FromForm] string? returnUrl, EmailLoginService emailLogin, HttpContext ctx) =>
        {
            var safeReturn = !string.IsNullOrWhiteSpace(returnUrl)
                             && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                ? returnUrl!
                : "/";

            var result = string.IsNullOrWhiteSpace(player)
                ? await emailLogin.ResolveAsync(email, ctx.RequestAborted)
                : await emailLogin.ResolvePlayerAsync(player, ctx.RequestAborted);
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

        // GET /user-photo/{name}  -> stream a stored profile photo from the private container. Open to everyone: the
        // sign-in page shows them before anyone signs in, and the names are hashes or random, so they reveal nothing.
        endpoints.MapGet($"{UserPhotoStorage.PhotoPathPrefix}/{{name}}", async (string name, UserPhotoStorage photoStorage, HttpContext ctx) =>
        {
            var photo = await photoStorage.OpenAsync(name, ctx.RequestAborted);
            if (photo is null)
            {
                return Results.NotFound();
            }

            ctx.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.Stream(photo.Value.Content, photo.Value.ContentType);
        });

        return endpoints;
    }
}
