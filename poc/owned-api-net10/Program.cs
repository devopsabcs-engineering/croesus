using System.Security.Claims;
using Croesus.OwnedApi.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;

var builder = WebApplication.CreateBuilder(args);

IdentityModelEventSource.ShowPII = false;

// Loaded before the host is built so a missing authority, tenant, client id, or scope name stops the
// process instead of producing an API that accepts tokens under an unintended audience.
var settings = OwnedApiSettings.Load(builder.Configuration);
builder.Services.AddSingleton(settings);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(DelegatedScopePolicy.Name, policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context => DelegatedScopePolicy.HasScope(context.User, settings.RequiredScope)));

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// The only route. There is no cookie scheme, no sign-in path, and no session: this application accepts a
// bearer token audienced to itself or it answers 401. That is what makes it a usable downstream for the
// reference BFF rather than a second front end.
app.MapGet("/api/profile", (ClaimsPrincipal user, HttpContext context) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(CallEvidenceProjection.Project(user, context.Request));
    })
    .RequireAuthorization(DelegatedScopePolicy.Name);

app.Run();
