using Croesus.LegacyNet452.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class OidcOptionsFactoryTests
    {
        [Fact]
        public void CreateUsesCodeOnlyPkceAndDoesNotPersistTokens()
        {
            var options = OidcOptionsFactory.Create(TestSettings.SingleTenant());

            Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
            Assert.Equal(OpenIdConnectResponseMode.Query, options.ResponseMode);
            Assert.True(options.UsePkce);
            Assert.True(options.RedeemCode);
            Assert.False(options.SaveTokens);
            Assert.Equal("openid profile", options.Scope);
            Assert.Equal(CookieOptionsFactory.AuthenticationType, options.SignInAsAuthenticationType);
        }

        [Fact]
        public void OrganizationsModeKeepsIssuerValidationEnabled()
        {
            var options = OidcOptionsFactory.Create(TestSettings.Organizations());

            Assert.True(options.TokenValidationParameters.ValidateIssuer);
            Assert.Equal(
                (Microsoft.IdentityModel.Tokens.IssuerValidator)TenantPolicy.ValidateOrganizationsIssuer,
                options.TokenValidationParameters.IssuerValidator);
        }
    }
}
