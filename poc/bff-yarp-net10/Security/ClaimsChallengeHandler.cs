using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Transforms;

namespace Croesus.BffYarp.Security;

/// <summary>
/// Turns a downstream claims challenge into a bounded, interaction-required result.
/// <para>
/// The rules this enforces, in order: the challenge is accepted only from a configured destination; the
/// claims payload is size-bounded and never echoed to the browser; the rejected token is not reused; the
/// original operation is never automatically replayed; and the response is a JSON result the frontend
/// converts into a deliberate top-level navigation, not an invisible fetch of a login page.
/// </para>
/// </summary>
internal sealed class ClaimsChallengeHandler(
    IDistributedCache cache,
    ProxyDestinationPolicy destinationPolicy,
    TimeProvider timeProvider,
    ILogger<ClaimsChallengeHandler> logger)
{
    internal const string CachePrefix = "croesus-bff-claims:";
    internal const string LoginPath = "/bff/login";
    private const int MaxClaimsLength = 4096;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads a claims challenge from a downstream 401. Returns null when the response is not a claims
    /// challenge, when the payload is oversized or malformed, or when the destination is not configured.
    /// </summary>
    public string? TryReadChallenge(ResponseTransformContext context)
    {
        if (context.ProxyResponse is null || (int)context.ProxyResponse.StatusCode != StatusCodes.Status401Unauthorized)
        {
            return null;
        }

        var destination = context.HttpContext.GetReverseProxyFeature().ProxiedDestination?.Model.Config.Address;
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var destinationUri)
            || !destinationPolicy.IsAllowedOrigin(destinationUri))
        {
            logger.LogWarning("Ignored a claims challenge from an unconfigured destination.");
            return null;
        }

        foreach (var header in context.ProxyResponse.Headers.WwwAuthenticate)
        {
            if (!string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
                || header.Parameter is null)
            {
                continue;
            }

            var claims = ReadQuotedParameter(header.Parameter, "claims");
            if (claims is null)
            {
                continue;
            }

            if (claims.Length > MaxClaimsLength)
            {
                logger.LogWarning("Rejected an oversized claims challenge from the owned API.");
                return null;
            }

            return claims;
        }

        return null;
    }

    /// <summary>Stores the claims payload server-side, bound to the current session, and returns its opaque id.</summary>
    public async Task<string> StoreChallengeAsync(ClaimsPrincipal principal, string claims)
    {
        var challengeId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var payload = JsonSerializer.SerializeToUtf8Bytes(new StoredChallenge(
            EvidenceCollector.SessionKey(principal),
            claims,
            timeProvider.GetUtcNow()));

        await cache.SetAsync(
            CachePrefix + challengeId,
            payload,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ChallengeLifetime })
            .ConfigureAwait(false);

        return challengeId;
    }

    /// <summary>
    /// Redeems a stored challenge for the calling session. A challenge raised for one session cannot be
    /// redeemed by another, and a redeemed challenge is removed so it cannot be replayed.
    /// </summary>
    public async Task<string?> RedeemChallengeAsync(ClaimsPrincipal principal, string? challengeId)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || challengeId.Length > 64)
        {
            return null;
        }

        var key = CachePrefix + challengeId;
        byte[]? payload;
        try
        {
            payload = await cache.GetAsync(key).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Claims challenge lookup failed ({ExceptionType}); treating it as absent.", exception.GetType().Name);
            return null;
        }

        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        await cache.RemoveAsync(key).ConfigureAwait(false);

        var stored = JsonSerializer.Deserialize<StoredChallenge>(payload);
        if (stored is null
            || !string.Equals(stored.SessionKey, EvidenceCollector.SessionKey(principal), StringComparison.Ordinal))
        {
            logger.LogWarning("Refused a claims challenge that belongs to a different session.");
            return null;
        }

        return stored.Claims;
    }

    /// <summary>
    /// Writes the bounded interaction-required body. The claims payload is not in it, because the browser
    /// does not need it and echoing it would widen the surface for no benefit.
    /// </summary>
    public static Task WriteInteractionRequiredAsync(HttpContext context, string? challengeId)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/json";

        var replaySafe = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        return context.Response.WriteAsJsonAsync(new
        {
            error = "interaction_required",
            challengeId,
            loginPath = challengeId is null ? LoginPath : $"{LoginPath}?challengeId={challengeId}",
            // The frontend performs a deliberate top-level navigation. Nothing here is retried automatically,
            // and an unsafe method is never replayed after interaction without an idempotency contract.
            navigationRequired = true,
            autoRetry = false,
            replaySafe
        });
    }

    private static string? ReadQuotedParameter(string parameter, string name)
    {
        var marker = name + "=\"";
        var start = parameter.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = parameter.IndexOf('"', start);
        return end < 0 ? null : parameter[start..end];
    }

    private sealed record StoredChallenge(string SessionKey, string Claims, DateTimeOffset RaisedAt);
}

/// <summary>
/// Response transform that intercepts a downstream claims challenge before any downstream body reaches the
/// browser, and replaces it with the bounded interaction-required result.
/// </summary>
internal sealed class ClaimsChallengeResponseTransform(ClaimsChallengeHandler handler) : ResponseTransform
{
    public override async ValueTask ApplyAsync(ResponseTransformContext context)
    {
        // The owned API's own Set-Cookie headers are not this application's session surface.
        context.HttpContext.Response.Headers.Remove(HeaderNames.SetCookie);

        if (context.ProxyResponse is not null
            && context.ProxyResponse.StatusCode is System.Net.HttpStatusCode.Redirect
                or System.Net.HttpStatusCode.MovedPermanently
                or System.Net.HttpStatusCode.TemporaryRedirect
                or System.Net.HttpStatusCode.PermanentRedirect)
        {
            // A downstream redirect would steer the browser off the guarded boundary. Fail closed instead.
            context.SuppressResponseBody = true;
            context.HttpContext.Response.Clear();
            context.HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        var claims = handler.TryReadChallenge(context);
        if (claims is null)
        {
            return;
        }

        context.SuppressResponseBody = true;
        var challengeId = await handler.StoreChallengeAsync(context.HttpContext.User, claims).ConfigureAwait(false);
        await ClaimsChallengeHandler.WriteInteractionRequiredAsync(context.HttpContext, challengeId).ConfigureAwait(false);
    }
}
