using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Croesus.BffYarp.Security;

/// <summary>
/// The gate every proxied request passes before the reverse proxy is allowed to see it.
/// <para>
/// It runs after routing, authentication, and authorization, and before the proxy endpoint executes, so a
/// rejection here means nothing was forwarded. The checks are ordered cheapest-first and each one fails
/// closed: an unknown method, an unauthenticated caller, a missing or invalid antiforgery token, or a token
/// acquisition that cannot complete all end the request here.
/// </para>
/// </summary>
internal sealed class ProxyBoundaryMiddleware(RequestDelegate next, ProxyDestinationPolicy destinationPolicy)
{
    private static readonly string[] AllowedMethods =
        ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ITokenAcquisition tokenAcquisition,
        BffAuthenticationSettings settings,
        EvidenceCollector evidence,
        ILogger<ProxyBoundaryMiddleware> logger)
    {
        if (!destinationPolicy.IsGuardedPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!AllowedMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
        {
            await WriteProblemAsync(context, StatusCodes.Status405MethodNotAllowed, "method_not_allowed");
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await ClaimsChallengeHandler.WriteInteractionRequiredAsync(context, challengeId: null);
            return;
        }

        var isStateChanging = !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method);
        if (isStateChanging && !await ValidateAntiforgeryAsync(context, antiforgery, logger))
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "antiforgery_validation_failed");
            return;
        }

        if (!await TryAcquireTokenAsync(context, tokenAcquisition, settings, evidence, logger))
        {
            return;
        }

        await next(context);

        evidence.RecordOwnedApiVerdict(context.User, context.Response.StatusCode);
    }

    internal static async Task<bool> ValidateAntiforgeryAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ILogger logger)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            logger.LogWarning(
                "Rejected a state-changing {Method} on {Path} with no valid antiforgery token.",
                context.Request.Method,
                context.Request.Path);
            return false;
        }
    }

    private static async Task<bool> TryAcquireTokenAsync(
        HttpContext context,
        ITokenAcquisition tokenAcquisition,
        BffAuthenticationSettings settings,
        EvidenceCollector evidence,
        ILogger logger)
    {
        var resource = ReadResource(settings.DownstreamScopes[0]);
        try
        {
            var result = await tokenAcquisition.GetAuthenticationResultForUserAsync(
                settings.DownstreamScopes,
                user: context.User);

            context.Items[AccessTokenTransform.TokenItemKey] = result.AccessToken;
            evidence.RecordAcquisition(
                context.User,
                resource,
                [.. result.Scopes],
                result.AuthenticationResultMetadata?.TokenSource.ToString() ?? "unknown",
                result.ExpiresOn,
                "succeeded");
            return true;
        }
        catch (Exception exception) when (exception is MicrosoftIdentityWebChallengeUserException or MsalUiRequiredException)
        {
            evidence.RecordAcquisition(context.User, resource, [], "none", null, "interaction-required");
            logger.LogInformation("Token acquisition requires interaction; the request was not forwarded.");
            await ClaimsChallengeHandler.WriteInteractionRequiredAsync(context, challengeId: null);
            return false;
        }
        catch (MsalException exception)
        {
            // Never fall back to a browser-supplied token or another cache entry; refuse the hop instead.
            evidence.RecordAcquisition(context.User, resource, [], "none", null, "failed");
            logger.LogError(
                "Token acquisition failed ({ExceptionType}); the request was not forwarded.",
                exception.GetType().Name);
            await WriteProblemAsync(context, StatusCodes.Status502BadGateway, "downstream_token_unavailable");
            return false;
        }
    }

    private static string ReadResource(string scope)
    {
        var separator = scope.LastIndexOf('/');
        return separator > 0 ? scope[..separator] : scope;
    }

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string error)
    {
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new { error });
    }
}
