using System.Security.Claims;
using System.Linq;
using Croesus.LegacyNet452.Authentication;
using Croesus.LegacyNet452.Web;
using Microsoft.Owin.Security;
using Newtonsoft.Json;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class SessionProjectionTests
    {
        [Fact]
        public void BrowserProjectionContainsOnlySessionMetadata()
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "Ada Lovelace"),
                    new Claim("tid", TestSettings.HomeTenantId),
                    new Claim("access_token", "must-not-be-projected"),
                    new Claim("refresh_token", "must-not-be-projected")
                },
                "validated");

            var json = JsonConvert.SerializeObject(SessionProjection.FromIdentity(identity));

            Assert.Contains("Ada Lovelace", json);
            Assert.Contains(TestSettings.HomeTenantId, json);
            Assert.DoesNotContain("access_token", json);
            Assert.DoesNotContain("refresh_token", json);
            Assert.DoesNotContain("must-not-be-projected", json);
        }

        [Fact]
        public void MinimalTicketDropsProtocolPropertiesButPreservesCallbackRouting()
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "user-id"),
                    new Claim(ClaimTypes.Name, "Ada Lovelace"),
                    new Claim("tid", TestSettings.HomeTenantId),
                    new Claim("access_token", "must-not-be-stored")
                },
                "validated");
            var properties = new AuthenticationProperties(
                new System.Collections.Generic.Dictionary<string, string>
                {
                    { ".Token.access_token", "must-not-be-stored" },
                    { "code_verifier", "must-not-be-stored" }
                });
            properties.RedirectUri = "/";

            var projected = SessionIdentityProjector.CreateMinimalTicket(
                new AuthenticationTicket(identity, properties));

            Assert.Equal(3, projected.Identity.Claims.Count());
            Assert.Equal("/", projected.Properties.RedirectUri);
            Assert.DoesNotContain(
                projected.Properties.Dictionary,
                item => item.Key.IndexOf("token", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Key.IndexOf("code", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Key.IndexOf("secret", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Key.IndexOf("verifier", System.StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.False(projected.Properties.IsPersistent);
            Assert.False(projected.Properties.AllowRefresh ?? true);
        }
    }
}
