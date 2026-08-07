using System;
using System.Configuration;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class LegacyAuthenticationSettingsTests
    {
        [Fact]
        public void SingleTenantModeUsesTenantSpecificAuthorityAndHomeTenant()
        {
            var settings = TestSettings.SingleTenant();

            Assert.False(settings.IsOrganizationsMode);
            Assert.Equal(
                "https://login.microsoftonline.com/" + TestSettings.HomeTenantId + "/v2.0",
                settings.Authority);
            Assert.True(settings.IsTenantAllowed(Guid.Parse(TestSettings.HomeTenantId)));
            Assert.False(settings.IsTenantAllowed(Guid.Parse(TestSettings.AllowedTenantId)));
        }

        [Fact]
        public void OrganizationsModeRequiresANonEmptyAllowlist()
        {
            Assert.Throws<ConfigurationErrorsException>(
                () => TestSettings.Create("Organizations", string.Empty));
        }

        [Fact]
        public void OrganizationsModeRejectsMalformedTenantIds()
        {
            Assert.Throws<ConfigurationErrorsException>(
                () => TestSettings.Create("Organizations", "not-a-guid"));
        }

        [Fact]
        public void RejectsEmptyClientId()
        {
            var exception = Assert.Throws<ConfigurationErrorsException>(
                () => CreateSettings(Guid.Empty.ToString("D"), TestSettings.HomeTenantId, string.Empty));

            Assert.Equal("ClientId must be a non-empty GUID.", exception.Message);
        }

        [Fact]
        public void RejectsEmptyTenantId()
        {
            var exception = Assert.Throws<ConfigurationErrorsException>(
                () => CreateSettings(
                    "22222222-2222-2222-2222-222222222222",
                    Guid.Empty.ToString("D"),
                    string.Empty));

            Assert.Equal("TenantId must be a non-empty GUID.", exception.Message);
        }

        [Fact]
        public void OrganizationsModeRejectsEmptyTenantIdInAllowlist()
        {
            var exception = Assert.Throws<ConfigurationErrorsException>(
                () => TestSettings.Create("Organizations", Guid.Empty.ToString("D")));

            Assert.Equal(
                "AllowedTenantIds must contain only non-empty GUID values.",
                exception.Message);
        }

        [Fact]
        public void OrganizationsModeUsesOrganizationsAuthorityAndAllowlist()
        {
            var settings = TestSettings.Organizations();

            Assert.True(settings.IsOrganizationsMode);
            Assert.Equal(
                "https://login.microsoftonline.com/organizations/v2.0",
                settings.Authority);
            Assert.True(settings.IsTenantAllowed(Guid.Parse(TestSettings.AllowedTenantId)));
            Assert.False(settings.IsTenantAllowed(Guid.Parse(TestSettings.HomeTenantId)));
        }

        private static Configuration.LegacyAuthenticationSettings CreateSettings(
            string clientId,
            string tenantId,
            string allowedTenantIds)
        {
            return Configuration.LegacyAuthenticationSettings.CreateAndValidate(
                clientId,
                tenantId,
                "SingleTenant",
                allowedTenantIds,
                "https://localhost:44352/signin-oidc",
                "https://localhost:44352/",
                Guid.NewGuid().ToString("N"));
        }
    }
}
