using System.Security.Claims;
using System.Text.Encodings.Web;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UniversalErp.Api.Security;

public sealed class FirebaseAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Firebase";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeaders))
        {
            return AuthenticateResult.NoResult();
        }

        var headerValue = authHeaders.ToString();
        if (string.IsNullOrWhiteSpace(headerValue) ||
            !headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = headerValue["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.Fail("Bearer token is empty.");
        }

        try
        {
            var authInstance = FirebaseAuth.DefaultInstance;
            if (authInstance is null)
            {
                Logger.LogError("FirebaseAuth.DefaultInstance is null. Firebase has not been initialized.");
                return AuthenticateResult.Fail("Firebase Authentication is not configured on the server.");
            }

            // Ensure clock skew against Google servers is synchronized
            NetworkSynchronizedClock.Initialize(Logger);
            NetworkSynchronizedClock.ApplyTo(authInstance, Logger);

            // Cryptographically verify signature, issuer, audience, expiry, and project ID
            var decodedToken = await authInstance.VerifyIdTokenAsync(token, checkRevoked: false);

            var claims = new List<Claim>
            {
                new("sub", decodedToken.Uid),
                new("erp:issuer", decodedToken.Issuer),
                new(ClaimTypes.NameIdentifier, decodedToken.Uid),
            };

            if (decodedToken.Claims.TryGetValue("email", out var email) && email is string emailStr)
            {
                claims.Add(new Claim("email", emailStr));
                claims.Add(new Claim(ClaimTypes.Email, emailStr));
            }

            if (decodedToken.Claims.TryGetValue("name", out var name) && name is string nameStr)
            {
                claims.Add(new Claim("name", nameStr));
                claims.Add(new Claim(ClaimTypes.Name, nameStr));
            }

            Logger.LogInformation("Successfully verified Firebase ID token for UID: {Uid}, Issuer: {Issuer}", decodedToken.Uid, decodedToken.Issuer);

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return AuthenticateResult.Success(ticket);
        }
        catch (FirebaseAuthException ex)
        {
            Logger.LogWarning(ex, "Firebase ID token verification failed: {ErrorCode} - {Message}", ex.AuthErrorCode, ex.Message);
            return AuthenticateResult.Fail($"Firebase token verification failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unexpected error verifying Firebase ID token.");
            return AuthenticateResult.Fail($"Token verification error: {ex.Message}");
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;

        var authResult = await HandleAuthenticateOnceSafeAsync();
        var failure = authResult.Failure;
        var errorDescription = failure switch
        {
            null => "Authentication token is missing or required.",
            _ => failure.Message.Replace("\"", "'")
        };

        Response.Headers.Append("WWW-Authenticate", $"Bearer error=\"invalid_token\", error_description=\"{errorDescription}\"");
    }
}
