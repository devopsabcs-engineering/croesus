using Croesus.Api.Telemetry;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        .WithMethods("GET"));
});

// Evidence layer: structured, claims-only logging of both OBO legs to Application Insights.
builder.Services.AddScoped<OboClaimLogger>();

builder.Services.AddControllers();

var app = builder.Build();

app.UseCors(spaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposed so the negative-control test project (WebApplicationFactory<Program>) can host the API.
public partial class Program { }
