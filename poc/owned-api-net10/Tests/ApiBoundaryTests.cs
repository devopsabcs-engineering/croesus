using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Croesus.OwnedApi.Tests;

/// <summary>
/// Boundary checks against the composed host. These assert the shape the reference BFF depends on: no
/// anonymous access, and no cookie scheme that could be used instead of a bearer token.
/// </summary>
public class ApiBoundaryTests : IClassFixture<ApiBoundaryTests.Factory>
{
    private readonly Factory _factory;

    public ApiBoundaryTests(Factory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/profile")]
    [InlineData("/")]
    [InlineData("/api/unmapped")]
    public async Task An_anonymous_request_is_rejected(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejection_challenges_for_a_bearer_token_and_never_redirects_to_a_sign_in_page()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/api/profile");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task No_cookie_is_ever_issued_because_the_api_holds_no_session()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/profile");

        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(TestConfiguration.Values()));

            return base.CreateHost(builder);
        }
    }
}
