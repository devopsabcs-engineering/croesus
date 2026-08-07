using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Croesus.ModernBff.Tests;

public sealed class SessionEndpointTests(ModernBffFactory factory)
    : IClassFixture<ModernBffFactory>
{
    [Fact]
    public async Task SessionResponseContainsOnlyAllowlistedMetadata()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/session");
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(document);
        var properties = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "displayName", "isAuthenticated", "tenantId" },
            properties);
        Assert.True(document.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("Test User", document.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(TestConfiguration.TenantId, document.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task SignOutRejectsARequestWithoutAntiforgeryProof()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsync("/signout", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
