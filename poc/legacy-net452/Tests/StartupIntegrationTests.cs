using System.Net;
using System;
using System.Threading.Tasks;
using Croesus.LegacyNet452.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Owin.Testing;
using Owin;
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

        [Fact]
        public async Task UnhandledDownstreamFailureReturnsGenericResponse()
        {
            using (var server = TestServer.Create(app =>
            {
                Startup.UseAuthenticationExceptionBoundary(app);
                app.Run(context =>
                {
                    throw new InvalidOperationException(
                        "code=authorization-code&state=state-value&client_secret=credential-value");
                });
            }))
            using (var response = await server.CreateRequest("/signin-oidc").GetAsync())
            {
                var content = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.Equal("text/plain", response.Content.Headers.ContentType.MediaType);
                Assert.Equal("Authentication could not be completed.", content);
                Assert.DoesNotContain("authorization-code", content);
                Assert.DoesNotContain("state-value", content);
                Assert.DoesNotContain("credential-value", content);
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