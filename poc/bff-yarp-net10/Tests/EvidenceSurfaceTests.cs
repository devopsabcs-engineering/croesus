using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Step 3.5 evidence: the evidence surface reports without disclosing.
/// <para>
/// Every non-disclosure test here first proves the secret it is looking for is genuinely present in
/// server-side session state. A "the body does not contain X" assertion against a session that never held X
/// proves nothing, so the presence check is what makes the absence check mean something.
/// </para>
/// <para>
/// Two independent leak detectors are used, because either alone has a blind spot. The sentinel is opaque
/// and has no JWT shape, so a JWT-regex sweep would miss it; the regex sweep, following the convention in
/// <c>api/Tests/ReplayEndpointTests.cs</c>, catches token-shaped material the sentinel comparison would miss.
/// </para>
/// </summary>
public sealed class EvidenceSurfaceTests
{
    // Same detector as api/Tests/ReplayEndpointTests.cs: three base64url segments, each long enough that
    // dotted type names in framework text cannot trip it.
    private static readonly Regex JwtShaped = new(
        @"[A-Za-z0-9_-]{24,}\.[A-Za-z0-9_-]{24,}\.[A-Za-z0-9_-]{24,}",
        RegexOptions.Compiled);

    /// <summary>The complete, intended shape of the evidence document.</summary>
    private static readonly string[] AllowedTopLevelFields =
    [
        "applicationRunId",
        "tokenCustody",
        "tokenCustodyDetail",
        "sessionCookie",
        "ownedApiAudienceValidation",
        "authenticationMethod",
        "authenticationTime",
        "grantedDelegatedScopes",
        "acquisitions"
    ];

    [Fact]
    public async Task Evidence_DoesNotDiscloseTheOpaqueSentinelHeldInTheSession()
    {
        using var factory = new BffFactory();
        var (client, body) = await ExercisedSessionAsync(factory);

        // The sentinel is not JWT-shaped, so only a literal comparison can catch it.
        Assert.DoesNotContain(TestConfiguration.SentinelSecret, body, StringComparison.Ordinal);

        // The server-acquired token embeds the sentinel and is forwarded on every hop; it must not surface.
        Assert.DoesNotContain(FakeTokenAcquisition.AccessToken, body, StringComparison.Ordinal);

        client.Dispose();
    }

    [Fact]
    public async Task TheSentinelIsGenuinelyHeldServerSide_SoTheAbsenceAssertionIsMeaningful()
    {
        using var factory = new BffFactory();
        var client = factory.CreateSecureClient();
        var signIn = await SessionHelpers.SignInAsync(client);

        var storeKey = SessionHelpers.TicketStoreKey(factory, signIn);
        var stored = factory.Services.GetRequiredService<IDistributedCache>().Get(storeKey);
        Assert.NotNull(stored);

        var serverTicket = TicketSerializer.Default.Deserialize(stored);
        Assert.NotNull(serverTicket);
        Assert.Equal(TestConfiguration.SentinelSecret, serverTicket.Properties.GetTokenValue("access_token"));
        Assert.Equal(TestConfiguration.SentinelSecret, serverTicket.Properties.GetTokenValue("refresh_token"));
    }

    [Fact]
    public async Task Evidence_ContainsNoJwtShapedSubstring()
    {
        using var factory = new BffFactory();
        var (client, body) = await ExercisedSessionAsync(factory);

        Assert.False(JwtShaped.IsMatch(body), "The evidence body contains a JWT-shaped substring.");

        client.Dispose();
    }

    [Fact]
    public async Task Evidence_ReportsSomething_SoTheLeakAssertionsAreNotVacuous()
    {
        using var factory = new BffFactory();
        var (client, body) = await ExercisedSessionAsync(factory);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // A projection that returned an empty document would pass every absence assertion above while
        // proving nothing, so the surface is shown to be populated.
        Assert.NotEmpty(root.GetProperty("applicationRunId").GetString()!);
        Assert.Equal("server-side", root.GetProperty("tokenCustody").GetString());
        Assert.NotEmpty(root.GetProperty("acquisitions").EnumerateArray().ToArray());
        Assert.Contains(
            TestConfiguration.DownstreamScope,
            root.GetProperty("grantedDelegatedScopes").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("accepted-by-owned-api", root.GetProperty("ownedApiAudienceValidation").GetString());

        client.Dispose();
    }

    [Fact]
    public async Task Evidence_SetsCacheControlNoStore()
    {
        using var factory = new BffFactory();
        using var client = await SessionHelpers.SignedInClientAsync(factory);

        var response = await client.GetAsync("/bff/evidence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Evidence_RequiresAnAuthenticatedSession()
    {
        using var factory = new BffFactory();
        using var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/bff/evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // No redirect to an identity provider, and no evidence document in the rejection body.
        Assert.Null(response.Headers.Location);
        Assert.Contains("interaction_required", body, StringComparison.Ordinal);
        foreach (var field in AllowedTopLevelFields)
        {
            Assert.DoesNotContain(field, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Evidence_DoesNotAssertThatAnOnBehalfOfExchangeOccurred()
    {
        using var factory = new BffFactory();
        var (client, body) = await ExercisedSessionAsync(factory);

        using var document = JsonDocument.Parse(body);
        var fields = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        // The surface is an allowlist. Pinning it exactly is what proves no On-Behalf-Of field was added,
        // rather than merely that one particular spelling is absent.
        Assert.Equal(AllowedTopLevelFields.Order(), fields.Order());

        foreach (var marker in new[] { "on-behalf-of", "onbehalfof", "obo", "jwt-bearer", "requested_token_use" })
        {
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        }

        // The one verdict the surface does carry is about the owned API, derived from its observed response.
        Assert.Equal(
            "accepted-by-owned-api",
            document.RootElement.GetProperty("ownedApiAudienceValidation").GetString());

        client.Dispose();
    }

    /// <summary>
    /// Signs in, drives one proxied call so a token is acquired and forwarded, then reads the evidence
    /// document. The session therefore holds the sentinel in the ticket store and has exercised the token
    /// path before any absence is asserted.
    /// </summary>
    private static async Task<(HttpClient Client, string Body)> ExercisedSessionAsync(BffFactory factory)
    {
        var client = await SessionHelpers.SignedInClientAsync(factory);

        var proxied = await client.GetAsync("/api/resource");
        Assert.Equal(HttpStatusCode.OK, proxied.StatusCode);
        var forwarded = Assert.Single(factory.Downstream.Requests);
        Assert.Contains(TestConfiguration.SentinelSecret, forwarded.Headers["Authorization"], StringComparison.Ordinal);

        var response = await client.GetAsync("/bff/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (client, await response.Content.ReadAsStringAsync());
    }
}
