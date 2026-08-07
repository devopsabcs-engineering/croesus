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
            failure.Contains("permitted only in Development or Poc", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionAcceptsAManagedIdentityBackedClientCredential()
    {
        var values = TestConfiguration.SingleTenantValues();
        values.Remove("AzureAd:ClientSecret");
        values["AzureAd:ClientCredentials:0:SourceType"] = "SignedAssertionFromManagedIdentity";

        var settings = BffAuthenticationSettings.Load(
            TestConfiguration.Build(values),
            Environments.Production);

        Assert.Equal(AuthorityMode.SingleTenant, settings.Mode);
    }

    [Fact]
    public void PocAcceptsAClientSecret()
    {
        var settings = BffAuthenticationSettings.Load(
            TestConfiguration.Build(TestConfiguration.SingleTenantValues()),
            "pOc");

        Assert.Equal(AuthorityMode.SingleTenant, settings.Mode);
    }

    [Fact]
    public void PocRequiresAClientSecretOrConfiguredClientCredential()
    {
        var values = TestConfiguration.SingleTenantValues();
        values.Remove("AzureAd:ClientSecret");

        var exception = Assert.Throws<OptionsValidationException>(() =>
            BffAuthenticationSettings.Load(TestConfiguration.Build(values), "Poc"));

        Assert.Contains(exception.Failures, failure =>
            failure.Contains("Development and Poc require", StringComparison.Ordinal));
    }

    [Fact]
    public void ArbitraryNonProductionEnvironmentRejectsAClientSecret()
    {
        var exception = Assert.Throws<OptionsValidationException>(() =>
            BffAuthenticationSettings.Load(
                TestConfiguration.Build(TestConfiguration.SingleTenantValues()),
                "Staging"));

        Assert.Contains(exception.Failures, failure =>
            failure.Contains("permitted only in Development or Poc", StringComparison.Ordinal));
    }
}
