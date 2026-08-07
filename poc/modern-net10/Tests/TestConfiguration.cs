using Microsoft.Extensions.Configuration;

namespace Croesus.ModernBff.Tests;

internal static class TestConfiguration
{
    public const string ClientId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string TenantId = "11111111-1111-1111-1111-111111111111";
    public const string OtherTenantId = "22222222-2222-2222-2222-222222222222";

    public static Dictionary<string, string?> SingleTenantValues() => new()
    {
        ["Authentication:Mode"] = "SingleTenant",
        ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        ["AzureAd:TenantId"] = TenantId,
        ["AzureAd:ClientId"] = ClientId,
        ["AzureAd:ClientSecret"] = "test-only-not-a-real-credential",
        ["AzureAd:CallbackPath"] = "/signin-oidc"
    };

    public static IConfiguration Build(IDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
