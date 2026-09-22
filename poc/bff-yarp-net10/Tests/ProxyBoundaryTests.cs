using System.Net;
using System.Net.Http.Json;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Step 3.3 evidence: what crosses the proxy boundary, and what is refused before anything crosses it.
/// </summary>
public sealed class ProxyBoundaryTests
{
    [Fact]
    public async Task ForwardedRequest_CarriesTheServerTokenAndNoBrowserCredential()
    {
        using var factory = new BffFactory();
        var client = await SessionHelpers.SignedInClientAsync(factory);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
        client.DefaultRequestHeaders.Add("X-Ms-Client-Principal-Id", "spoofed-principal");

        var response = await client.GetAsync("/api/resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(factory.Downstream.Requests);
        Assert.Equal($"Bearer {FakeTokenAcquisition.AccessToken}", forwarded.Headers["Authorization"]);
        Assert.False(forwarded.Headers.ContainsKey("Cookie"));
        Assert.False(forwarded.Headers.ContainsKey("X-Ms-Client-Principal-Id"));
        Assert.DoesNotContain(
            forwarded.Headers,
            header => header.Value.Contains("203.0.113.9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForwardedResponse_DoesNotPropagateDownstreamSetCookie()
    {
        using var factory = new BffFactory();
        var client = await SessionHelpers.SignedInClientAsync(factory);

        var response = await client.GetAsync("/api/resource");

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task StateChangingRequest_WithoutAnAntiforgeryToken_IsRejectedAndNeverForwarded()
    {
        using var factory = new BffFactory();
        var client = await SessionHelpers.SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/resource", new { value = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Downstream.Requests);
        Assert.Contains("antiforgery_validation_failed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StateChangingRequest_WithAnInvalidAntiforgeryToken_IsRejectedAndNeverForwarded()
    {
        using var factory = new BffFactory();
        var client = await SessionHelpers.SignedInClientAsync(factory);
        var (headerName, _) = await SessionHelpers.AntiforgeryTokenAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resource")
        {
            Content = JsonContent.Create(new { value = 1 })
        };
        request.Headers.Add(headerName, "not-the-issued-token");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Downstream.Requests);
    }

    [Fact]
    public async Task StateChangingRequest_WithAValidAntiforgeryToken_IsForwarded()
    {
        using var factory = new BffFactory();
        var client = await SessionHelpers.SignedInClientAsync(factory);
        var (headerName, token) = await SessionHelpers.AntiforgeryTokenAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/resource")
        {
            Content = JsonContent.Create(new { value = 1 })
        };
        request.Headers.Add(headerName, token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(factory.Downstream.Requests);
        Assert.Equal("POST", forwarded.Method);
    }

    [Fact]
    public async Task UnauthenticatedRequest_IsNotForwardedAndDoesNotRedirectAFetchCall()
    {
        using var factory = new BffFactory();
        var client = factory.CreateSecureClient();

        var response = await client.GetAsync("/api/resource");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(factory.Downstream.Requests);
        Assert.Contains("interaction_required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestOutsideTheConfiguredRoute_IsNotForwarded()
    {
        using var factory = new BffFactory();
        var client = await SessionHelpers.SignedInClientAsync(factory);

        var response = await client.GetAsync("/elsewhere/resource");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Downstream.Requests);
    }

    [Fact]
    public void DestinationOutsideTheAllowlist_FailsStartup()
    {
        using var factory = new UnlistedDestinationFactory();

        var exception = Record.Exception(() => factory.CreateSecureClient());

        Assert.NotNull(exception);
        Assert.Contains("AllowedDestinationOrigins", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PlaintextDestination_FailsStartup()
    {
        using var factory = new PlaintextDestinationFactory();

        var exception = Record.Exception(() => factory.CreateSecureClient());

        Assert.NotNull(exception);
        Assert.Contains("absolute HTTPS address", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RouteOutsideTheGuardedPrefix_FailsStartup()
    {
        using var factory = new UnguardedRouteFactory();

        var exception = Record.Exception(() => factory.CreateSecureClient());

        Assert.NotNull(exception);
        Assert.Contains("outside ProxyPolicy:RoutePrefixes", exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class UnlistedDestinationFactory : BffFactory
    {
        protected override Dictionary<string, string?> ConfigurationValues()
        {
            var values = base.ConfigurationValues();
            values["ReverseProxy:Clusters:owned-api:Destinations:primary:Address"] = "https://attacker.invalid";
            return values;
        }
    }

    private sealed class PlaintextDestinationFactory : BffFactory
    {
        protected override Dictionary<string, string?> ConfigurationValues()
        {
            var values = base.ConfigurationValues();
            values["ReverseProxy:Clusters:owned-api:Destinations:primary:Address"] = "http://owned-api.invalid";
            return values;
        }
    }

    private sealed class UnguardedRouteFactory : BffFactory
    {
        protected override Dictionary<string, string?> ConfigurationValues()
        {
            var values = base.ConfigurationValues();
            values["ReverseProxy:Routes:owned-api:Match:Path"] = "/{**catch-all}";
            return values;
        }
    }
}
