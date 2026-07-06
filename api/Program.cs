using System.Reflection;
using Croesus.Api.Controllers;
using Croesus.Api.Telemetry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// Application Insights telemetry. The connection string is read from configuration key
// APPLICATIONINSIGHTS_CONNECTION_STRING, which is supplied at runtime through a Key Vault
// reference resolved by the App Service managed identity. It is never committed to source.
builder.Services.AddApplicationInsightsTelemetry();

// Microsoft.Identity.Web fluent chain (correct-by-construction OBO):
//   AddMicrosoftIdentityWebApi          -> validates the inbound API token (issuer, audience, signature).
//   EnableTokenAcquisitionToCallDownstreamApi -> turns on the On-Behalf-Of token machinery.
//   AddMicrosoftGraph                   -> a GraphServiceClient that performs the OBO exchange and /me call.
//   AddInMemoryTokenCaches              -> caches the acquired downstream (Graph) tokens.
// The confidential-client credential is a Key Vault certificate, configured under
// AzureAd:ClientCredentials (SourceType=KeyVault) — no code change, no inline secret.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration, "AzureAd")
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddMicrosoftGraph(builder.Configuration.GetSection("Graph"))
        .AddInMemoryTokenCaches();

builder.Services.AddAuthorization();

// CORS: the SPA is served from a different origin than this API, so the browser issues a
// preflighted cross-origin request for GET /api/me (it carries an Authorization header).
// Allowed origins come from configuration key Cors:AllowedOrigins, supplied at runtime via the
// App Service setting Cors__AllowedOrigins__<n> (e.g. the deployed SPA URL). No origins are
// hard-coded; an empty list means no cross-origin caller is permitted.
const string spaCorsPolicy = "SpaCors";
var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(spaCorsPolicy, policy => policy
        .WithOrigins(corsAllowedOrigins)
        .AllowAnyHeader()
        // POST is required by the gated Tier 2 replay endpoint; GET serves /api/me.
        .WithMethods("GET", "POST"));
});

// Evidence layer: structured, claims-only logging of both OBO legs to Application Insights.
builder.Services.AddScoped<OboClaimLogger>();

// Server-side HttpClient used by the gated Tier 2 replay endpoint to re-present a forwarded token.
builder.Services.AddHttpClient();

// Reversible Tier 2 gate: the deliberate token-replay endpoint is only MAPPED when Demo:EnableReplay is
// true. When false (the default), an application feature provider removes ReplayController from the MVC
// model so its route is absent entirely (POST /api/replay -> 404), rather than present-but-refusing.
// The flag is evaluated lazily (when the controller feature is populated, after configuration is fully
// merged) so the value reflects the final configuration, including test-host overrides.
var configuration = builder.Configuration;
builder.Services
    .AddControllers()
    .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(
        new ExcludeControllerFeatureProvider(
            typeof(ReplayController),
            () => !configuration.GetValue<bool>("Demo:EnableReplay"))));

var app = builder.Build();

app.UseCors(spaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so the negative-control test project (WebApplicationFactory<Program>) can host the API.
public partial class Program { }

/// <summary>
/// Removes a single controller from the discovered MVC model when a lazily-evaluated predicate returns
/// true. Used to make the reversible Tier 2 replay endpoint absent (route unmapped) when
/// <c>Demo:EnableReplay</c> is false, so it returns 404 rather than existing and refusing. The predicate is
/// evaluated at feature-population time (after configuration is fully merged), so it reflects the final
/// configuration value.
/// </summary>
internal sealed class ExcludeControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly TypeInfo _excluded;
    private readonly Func<bool> _shouldExclude;

    public ExcludeControllerFeatureProvider(Type excluded, Func<bool> shouldExclude)
    {
        _excluded = excluded.GetTypeInfo();
        _shouldExclude = shouldExclude;
    }

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        if (!_shouldExclude())
        {
            return;
        }

        for (var i = feature.Controllers.Count - 1; i >= 0; i--)
        {
            if (feature.Controllers[i] == _excluded)
            {
                feature.Controllers.RemoveAt(i);
            }
        }
    }
}
