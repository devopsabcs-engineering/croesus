using System.Collections.Generic;
using System.Threading.Tasks;
using Croesus.LegacyNet452.Authentication;
using Microsoft.Owin.Builder;
using Microsoft.Owin.Security;
using Owin;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class StateProtectionTests
    {
        [Fact]
        public void KatanaPipelineInitializesProtectedStateFormat()
        {
            var app = new AppBuilder();
            app.Properties["host.AppName"] = "Croesus.LegacyNet452.Tests";
            var oidcOptions = OidcOptionsFactory.Create(TestSettings.SingleTenant());

            app.SetDefaultSignInAsAuthenticationType(CookieOptionsFactory.AuthenticationType);
            app.UseCookieAuthentication(CookieOptionsFactory.Create());
            app.UseOpenIdConnectAuthentication(oidcOptions);
            app.Build<System.Func<IDictionary<string, object>, Task>>();

            Assert.NotNull(oidcOptions.StateDataFormat);
        }
    }
}
