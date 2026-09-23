using Microsoft.Extensions.Configuration;

namespace Croesus.OwnedApi.Tests;

internal static class TestConfiguration
{
    public const string ClientId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string RequiredScope = "access_as_user";

    public static Dictionary<string, string?> Values() => new()
    {
        ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        ["AzureAd:TenantId"] = TenantId,
        ["AzureAd:ClientId"] = ClientId,
        ["Authorization:RequiredScope"] = RequiredScope
    };

    public static IConfiguration Build(IDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
