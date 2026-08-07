using Croesus.LegacyNet452.Authentication;
using Microsoft.Owin;
using Microsoft.Owin.Host.SystemWeb;
using Microsoft.Owin.Security.Cookies;
using Xunit;

namespace Croesus.LegacyNet452.Tests
{
    public sealed class CookieOptionsFactoryTests
    {
        [Fact]
        public void CreateUsesSecureSystemWebCookieCustody()
        {
            var options = CookieOptionsFactory.Create();

            Assert.True(options.CookieHttpOnly);
            Assert.Equal(CookieSecureOption.Always, options.CookieSecure);
            Assert.Equal(SameSiteMode.Lax, options.CookieSameSite);
            Assert.IsType<SystemWebCookieManager>(options.CookieManager);
            Assert.False(options.SlidingExpiration);
            Assert.StartsWith("__Host-", options.CookieName);
        }
    }
}
