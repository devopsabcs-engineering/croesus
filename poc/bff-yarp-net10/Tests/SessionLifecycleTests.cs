using System.Net.Http.Json;
using System.Security.Claims;
using Croesus.BffYarp.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Croesus.BffYarp.Tests;

/// <summary>Shared helpers for driving an authenticated session through the real pipeline.</summary>
internal static class SessionHelpers
{
    public const string SessionCookiePrefix = "__Host-Croesus.BffYarp.Session=";
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    public static async Task<HttpClient> SignedInClientAsync(BffFactory factory, string? sessionId = null)
    {
        var client = factory.CreateSecureClient();
        await SignInAsync(client, sessionId);
        return client;
    }

    public static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string? sessionId = null)
    {
        var path = sessionId is null
            ? TestSignInStartupFilter.SignInPath
            : $"{TestSignInStartupFilter.SignInPath}?sid={Uri.EscapeDataString(sessionId)}";
        var response = await client.GetAsync(path);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        return response;
    }

    public static async Task<(string HeaderName, string Token)> AntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/bff/antiforgery");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AntiforgeryPayload>();
        Assert.NotNull(payload);
        return (payload.HeaderName!, payload.RequestToken!);
    }

    /// <summary>Recovers the ticket store key the browser is holding, using the application's own key ring.</summary>
    public static string TicketStoreKey(BffFactory factory, HttpResponseMessage signInResponse)
    {
        var setCookie = signInResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(SessionCookiePrefix, StringComparison.Ordinal));
        var encoded = setCookie.Split(';')[0].Split('=', 2)[1];

        var protector = factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(
                "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
                CookieAuthenticationDefaults.AuthenticationScheme,
                "v2");

        var ticket = new TicketDataFormat(protector).Unprotect(encoded);
        Assert.NotNull(ticket);
        return ticket.Principal.FindFirstValue(SessionIdClaim)!;
    }

    private sealed record AntiforgeryPayload(string? RequestToken, string? HeaderName);
}

/// <summary>
/// Step 3.4 evidence: renewal, refusal, challenge handling, and logout. Each path is asserted to fail closed
/// rather than to fall back to anything the browser supplied.
/// </summary>
public sealed class SessionLifecycleTests
{
    [Fact]
    public async Task Logout_InvalidatesTheServerTicketSoTheCapturedCookieStopsWorking()
    {
        using var factory = new BffFactory();
        var client = factory.CreateSecureClient();
        var signIn = await SessionHelpers.SignInAsync(client);
        var storeKey = SessionHelpers.TicketStoreKey(factory, signIn);
        var (headerName, token) = await SessionHelpers.AntiforgeryTokenAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.Add(headerName, token);
        var logout = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Null(factory.Services.GetRequiredService<IDistributedCache>().Get(storeKey));

        // The captured cookie is replayed deliberately; it must no longer resolve to a session.
        var replay = factory.CreateSecureClient();
        replay.DefaultRequestHeaders.Add("Cookie", ReplayCookie(signIn));
        var afterLogout = await replay.GetAsync("/bff/evidence");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutAnAntiforgeryToken_IsRejectedAndTheSessionSurvives()
    {
        using var factory = new BffFactory();
        var client = factory.CreateSecureClient();
        var signIn = await SessionHelpers.SignInAsync(client);
        var storeKey = SessionHelpers.TicketStoreKey(factory, signIn);

        var logout = await client.PostAsync("/bff/logout", content: null);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.NotNull(factory.Services.GetRequiredService<IDistributedCache>().Get(storeKey));
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/bff/evidence")).StatusCode);
    }

    [Fact]
    public async Task RevokedSession_WhoseStoreEntryIsGone_IsTreatedAsUnauthenticated()
    {
        using var factory = new BffFactory();
        var client = factory.CreateSecureClient();
        var signIn = await SessionHelpers.SignInAsync(client);
        var storeKey = SessionHelpers.TicketStoreKey(factory, signIn);

        // Server-side revocation or an expired entry: the browser still holds a valid-looking cookie.
        factory.Services.GetRequiredService<IDistributedCache>().Remove(storeKey);

        var response = await client.GetAsync("/api/resource");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.Downstream.Requests);
    }

    [Fact]
    public async Task InteractionRequired_ReturnsABoundedJsonResultRatherThanALoginPage()
    {
        using var factory = new BffFactory();
        factory.TokenAcquisition.NextBehavior = FakeTokenAcquisition.Behavior.InteractionRequired;
        var client = await SessionHelpers.SignedInClientAsync(factory);

        var response = await client.GetAsync("/api/resource");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("interaction_required", body, StringComparison.Ordinal);
        Assert.Contains("\"autoRetry\":false", body, StringComparison.Ordinal);
        Assert.Empty(factory.Downstream.Requests);
    }

    [Fact]
    public async Task TokenServiceFailure_FailsClosedWithoutForwardingAnything()
    {
        using var factory = new BffFactory();
        factory.TokenAcquisition.NextBehavior = FakeTokenAcquisition.Behavior.ServiceFailure;
        var client = await SessionHelpers.SignedInClientAsync(factory);

        var response = await client.GetAsync("/api/resource");

        Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains(
            "downstream_token_unavailable",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Empty(factory.Downstream.Requests);
    }

    [Fact]
    public async Task ConcurrentRequests_EachCarryTheServerTokenAndNoneLeakAcross()
    {
        using var factory = new BffFactory();
        var client = await SessionHelpers.SignedInClientAsync(factory);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            client.GetAsync($"/api/resource/{index}")));

        Assert.All(responses, response => Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(8, factory.Downstream.Requests.Count);
        Assert.All(factory.Downstream.Requests, forwarded =>
        {
            Assert.Equal($"Bearer {FakeTokenAcquisition.AccessToken}", forwarded.Headers["Authorization"]);
            Assert.False(forwarded.Headers.ContainsKey("Cookie"));
        });
    }

    [Fact]
    public async Task DownstreamClaimsChallenge_BecomesABoundedInteractionRequiredResultWithoutReplay()
    {
        using var factory = new BffFactory();
        factory.Downstream.NextStatusCode = System.Net.HttpStatusCode.Unauthorized;
        factory.Downstream.NextWwwAuthenticate =
            $"Bearer realm=\"\", error=\"insufficient_claims\", claims=\"{TestConfiguration.SentinelSecret}\"";
        var client = await SessionHelpers.SignedInClientAsync(factory);
        var (headerName, token) = await SessionHelpers.AntiforgeryTokenAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resource")
        {
            Content = JsonContent.Create(new { value = 1 })
        };
        request.Headers.Add(headerName, token);
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        // Exactly one downstream invocation: an unsafe operation is never replayed after a challenge.
        Assert.Single(factory.Downstream.Requests);
        Assert.Contains("interaction_required", body, StringComparison.Ordinal);
        Assert.Contains("\"autoRetry\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"replaySafe\":false", body, StringComparison.Ordinal);
        // The claims payload stays server-side; only its opaque handle is returned.
        Assert.DoesNotContain(TestConfiguration.SentinelSecret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChallengeContinuation_CarriesTheClaimsRequestToTheAuthorizeEndpoint()
    {
        using var factory = new SyntheticOidcFactory();
        factory.Downstream.NextStatusCode = System.Net.HttpStatusCode.Unauthorized;
        factory.Downstream.NextWwwAuthenticate =
            "Bearer realm=\"\", error=\"insufficient_claims\", claims=\"eyJhY2Nlc3NfdG9rZW4iOnt9fQ\"";
        var client = await SessionHelpers.SignedInClientAsync(factory);

        var challenged = await client.GetAsync("/api/resource");
        var challengeId = await ReadChallengeIdAsync(challenged);

        var continuation = await client.GetAsync($"/bff/login?challengeId={challengeId}");

        Assert.Equal(System.Net.HttpStatusCode.Found, continuation.StatusCode);
        Assert.Contains("claims=", continuation.Headers.Location!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChallengeRaisedForOneSession_CannotBeRedeemedByAnother()
    {
        using var factory = new SyntheticOidcFactory();
        factory.Downstream.NextStatusCode = System.Net.HttpStatusCode.Unauthorized;
        factory.Downstream.NextWwwAuthenticate =
            "Bearer realm=\"\", error=\"insufficient_claims\", claims=\"eyJhY2Nlc3NfdG9rZW4iOnt9fQ\"";
        var owner = await SessionHelpers.SignedInClientAsync(factory, "session-one");
        var challengeId = await ReadChallengeIdAsync(await owner.GetAsync("/api/resource"));

        var other = await SessionHelpers.SignedInClientAsync(factory, "session-two");
        var continuation = await other.GetAsync($"/bff/login?challengeId={challengeId}");

        Assert.Equal(System.Net.HttpStatusCode.Found, continuation.StatusCode);
        Assert.DoesNotContain("claims=", continuation.Headers.Location!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void ChallengeCapabilityIsAdvertisedOnlyBecauseTheContinuationPathExists()
    {
        // The claims request reaches the authorize endpoint, proven by
        // ChallengeContinuation_CarriesTheClaimsRequestToTheAuthorizeEndpoint. This test records the
        // dependency so the capability advertisement and the implementation stay tied together.
        Assert.Equal("/bff/login", ClaimsChallengeHandler.LoginPath);
    }

    private static async Task<string> ReadChallengeIdAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<ChallengePayload>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrEmpty(payload.ChallengeId));
        return payload.ChallengeId!;
    }

    private static string ReplayCookie(HttpResponseMessage signInResponse) =>
        signInResponse.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(SessionHelpers.SessionCookiePrefix, StringComparison.Ordinal))
            .Split(';')[0];

    private sealed record ChallengePayload(string? Error, string? ChallengeId, string? LoginPath);
}
