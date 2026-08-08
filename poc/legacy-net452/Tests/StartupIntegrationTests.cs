using System.Net;
using System.Threading.Tasks;
using Croesus.LegacyNet452.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Owin.Testing;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class StartupIntegrationTests
    {
        [Fact]
        public async Task AnonymousSessionReturnsDirectUnauthorizedResponse()
        {
            using (var server = CreateServer())
            using (var response = await server.CreateRequest("/api/session").GetAsync())
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.False(response.Headers.Contains("Location"));
            }
        }

        [Fact]
        public async Task SignInRemainsAnExplicitOidcChallenge()
        {
            using (var server = CreateServer())
            using (var response = await server.CreateRequest("/signin").GetAsync())
            {
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.NotNull(response.Headers.Location);
                Assert.Equal("login.microsoftonline.com", response.Headers.Location.Host);
            }
        }

        [Fact]
        public async Task AnonymousRootIncludesSignedOutMessage()
        {
            using (var server = CreateServer())
            using (var response = await server.CreateRequest("/").GetAsync())
            {
                var content = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("Sign in required", content);
                Assert.DoesNotContain("Failed to fetch", content);
            }
        }

        private static TestServer CreateServer()
        {
            var options = OidcOptionsFactory.Create(TestSettings.SingleTenant());
            options.Configuration = new OpenIdConnectConfiguration
            {
                AuthorizationEndpoint =
                    "https://login.microsoftonline.com/test/oauth2/v2.0/authorize"
            };

            return TestServer.Create(app => Startup.ConfigureApplication(app, options));
        }
    }
}