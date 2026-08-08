using System;
using Croesus.LegacyNet452.Authentication;
using Microsoft.Owin;
using Microsoft.Owin.Host.SystemWeb;
using Microsoft.Owin.Security;
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CookieExceptionCallbackPreservesRethrow(bool rethrow)
        {
            var options = CookieOptionsFactory.Create();
            var provider = Assert.IsType<CookieAuthenticationProvider>(options.Provider);
            var exceptionContext = new CookieExceptionContext(
                new OwinContext(),
                options,
                CookieExceptionContext.ExceptionLocation.ApplyResponseGrant,
                new InvalidOperationException("secret-bearing-message"),
                (AuthenticationTicket)null)
            {
                Rethrow = rethrow
            };

            Assert.NotNull(provider.OnException);

            provider.OnException(exceptionContext);

            Assert.Equal(rethrow, exceptionContext.Rethrow);
        }
    }
}
