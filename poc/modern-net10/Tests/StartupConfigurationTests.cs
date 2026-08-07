using Croesus.ModernBff.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Croesus.ModernBff.Tests;

public sealed class StartupConfigurationTests
{
    [Fact]
    public void SingleTenantIsTheDefaultMode()
    {
        var values = TestConfiguration.SingleTenantValues();
        values.Remove("Authentication:Mode");

        var settings = BffAuthenticationSettings.Load(
            TestConfiguration.Build(values),
            Environments.Development);

        Assert.Equal(AuthorityMode.SingleTenant, settings.Mode);
        Assert.Equal(Guid.Parse(TestConfiguration.TenantId), settings.SingleTenantId);
    }

    [Fact]
    public void OrganizationsModeRequiresANonEmptyAllowlistAtStartup()
    {
        var values = TestConfiguration.SingleTenantValues();
        values["Authentication:Mode"] = "Organizations";
        values["AzureAd:TenantId"] = "organizations";

        var exception = Assert.Throws<OptionsValidationException>(() =>
            BffAuthenticationSettings.Load(
                TestConfiguration.Build(values),
                Environments.Development));

        Assert.Contains(exception.Failures, failure =>
            failure.Contains("at least one allowed tenant GUID", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionRejectsAClientSecret()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            BffAuthenticationSettings.Load(
                TestConfiguration.Build(TestConfiguration.SingleTenantValues()),
                Environments.Production));

        Assert.Contains(exception.Failures, failure =>
            failure.Contains("permitted only in Development", StringComparison.Ordinal));
    }
}
