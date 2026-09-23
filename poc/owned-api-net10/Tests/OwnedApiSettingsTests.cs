using Croesus.OwnedApi.Security;
using Microsoft.Extensions.Options;

namespace Croesus.OwnedApi.Tests;

public class OwnedApiSettingsTests
{
    [Fact]
    public void Load_accepts_a_complete_single_tenant_configuration()
    {
        var settings = OwnedApiSettings.Load(TestConfiguration.Build(TestConfiguration.Values()));

        Assert.Equal(Guid.Parse(TestConfiguration.TenantId), settings.TenantId);
        Assert.Equal(Guid.Parse(TestConfiguration.ClientId), settings.ClientId);
        Assert.Equal(TestConfiguration.RequiredScope, settings.RequiredScope);
        Assert.Equal("https://login.microsoftonline.com/", settings.Instance.AbsoluteUri);
    }

    [Theory]
    [InlineData("AzureAd:Instance", "http://login.microsoftonline.com/")]
    [InlineData("AzureAd:Instance", "")]
    [InlineData("AzureAd:TenantId", "organizations")]
    [InlineData("AzureAd:TenantId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("AzureAd:ClientId", "not-a-guid")]
    [InlineData("Authorization:RequiredScope", "")]
    [InlineData("Authorization:RequiredScope", "access_as_user User.Read")]
    public void Load_fails_closed_on_a_malformed_value(string key, string value)
    {
        var values = TestConfiguration.Values();
        values[key] = value;

        Assert.Throws<OptionsValidationException>(() =>
            OwnedApiSettings.Load(TestConfiguration.Build(values)));
    }

    [Fact]
    public void Load_reports_every_failure_rather_than_only_the_first()
    {
        var values = TestConfiguration.Values();
        values["AzureAd:ClientId"] = "not-a-guid";
        values["Authorization:RequiredScope"] = "";

        var exception = Assert.Throws<OptionsValidationException>(() =>
            OwnedApiSettings.Load(TestConfiguration.Build(values)));

        Assert.Equal(2, exception.Failures.Count());
    }
}
