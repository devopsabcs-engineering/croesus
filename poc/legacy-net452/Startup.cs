using System.Net;
using System.Security.Claims;
using Croesus.LegacyNet452.Authentication;
using Croesus.LegacyNet452.Configuration;
using Croesus.LegacyNet452.Web;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OpenIdConnect;
using Newtonsoft.Json;
using Owin;

[assembly: OwinStartup(typeof(Croesus.LegacyNet452.Startup))]

namespace Croesus.LegacyNet452
{
    public sealed class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var settings = LegacyAuthenticationSettings.LoadAndValidate();

            app.SetDefaultSignInAsAuthenticationType(
                CookieOptionsFactory.AuthenticationType);
            app.UseCookieAuthentication(CookieOptionsFactory.Create());
            app.UseOpenIdConnectAuthentication(OidcOptionsFactory.Create(settings));

            app.Use(async (context, next) =>
            {
                if (context.Request.Path == new PathString("/signin"))
                {
                    context.Authentication.Challenge(
                        new AuthenticationProperties { RedirectUri = "/" },
                        OpenIdConnectAuthenticationDefaults.AuthenticationType);
                    return;
                }

                if (context.Request.Path == new PathString("/api/session"))
                {
                    var identity = context.Authentication.User == null
                        ? null
                        : context.Authentication.User.Identity as ClaimsIdentity;
                    if (identity == null || !identity.IsAuthenticated)
                    {
                        context.Response.StatusCode = 401;
                        context.Response.Headers.Set("Cache-Control", "no-store");
                        return;
                    }

                    context.Response.ContentType = "application/json";
                    context.Response.Headers.Set("Cache-Control", "no-store");
                    await context.Response.WriteAsync(
                        JsonConvert.SerializeObject(SessionProjection.FromIdentity(identity)));
                    return;
                }

                if (context.Request.Path == PathString.Empty ||
                    context.Request.Path == new PathString("/"))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.Set("Cache-Control", "no-store");
                    await context.Response.WriteAsync(
                        "<!doctype html><html><body><h1>Legacy BFF session</h1>" +
                        "<p><a href=\"/signin\">Sign in</a></p>" +
                        "<pre id=\"session\"></pre>" +
                        "<script>fetch('/api/session',{credentials:'same-origin'})" +
                        ".then(function(r){if(!r.ok){throw new Error('Sign in required');}" +
                        "return r.json();})" +
                        ".then(function(s){document.getElementById('session').textContent=" +
                        "JSON.stringify(s,null,2);})" +
                        ".catch(function(e){document.getElementById('session').textContent=" +
                        "e.message;});</script></body></html>");
                    return;
                }

                await next();
            });
        }
    }
}
