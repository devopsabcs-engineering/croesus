using Microsoft.Extensions.Options;

namespace Croesus.ModernBff.Security;

internal enum AuthorityMode
{
    SingleTenant,
    Organizations
}

internal sealed class BffAuthenticationSettings
{
    private BffAuthenticationSettings(
        AuthorityMode mode,
        Uri instance,
        Guid clientId,
        Guid? singleTenantId,
        IReadOnlySet<Guid> allowedTenantIds)
    {
        Mode = mode;
        Instance = instance;
        ClientId = clientId;
        SingleTenantId = singleTenantId;
        AllowedTenantIds = allowedTenantIds;
    }

    public AuthorityMode Mode { get; }

    public Uri Instance { get; }

    public Guid ClientId { get; }

    public Guid? SingleTenantId { get; }

    public IReadOnlySet<Guid> AllowedTenantIds { get; }

    public static BffAuthenticationSettings Load(IConfiguration configuration, string environmentName)
    {
        var failures = new List<string>();
        var modeText = configuration["Authentication:Mode"] ?? nameof(AuthorityMode.SingleTenant);
        if (!Enum.TryParse<AuthorityMode>(modeText, ignoreCase: true, out var mode))
        {
            failures.Add("Authentication:Mode must be SingleTenant or Organizations.");
        }

        var instanceText = configuration["AzureAd:Instance"];
        if (!Uri.TryCreate(instanceText, UriKind.Absolute, out var instance)
            || instance.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("AzureAd:Instance must be an absolute HTTPS URI.");
            instance = new Uri("https://login.microsoftonline.com/");
        }

        if (!Guid.TryParse(configuration["AzureAd:ClientId"], out var clientId)
            || clientId == Guid.Empty)
        {
            failures.Add("AzureAd:ClientId must be a non-empty GUID.");
        }

        var allowedTenantIds = new HashSet<Guid>();
        foreach (var configuredTenantId in configuration
                     .GetSection("Authentication:AllowedTenantIds")
                     .Get<string[]>()
                 ?? [])
        {
            if (!Guid.TryParse(configuredTenantId, out var allowedTenantId)
                || allowedTenantId == Guid.Empty)
            {
                failures.Add("Every Authentication:AllowedTenantIds entry must be a non-empty GUID.");
                continue;
            }

            allowedTenantIds.Add(allowedTenantId);
        }

        Guid? singleTenantId = null;
        var configuredAuthorityTenant = configuration["AzureAd:TenantId"];
        if (mode == AuthorityMode.SingleTenant)
        {
            if (!Guid.TryParse(configuredAuthorityTenant, out var parsedTenantId)
                || parsedTenantId == Guid.Empty)
            {
                failures.Add("AzureAd:TenantId must be a non-empty GUID in single-tenant mode.");
            }
            else
            {
                singleTenantId = parsedTenantId;
            }
        }
        else
        {
            if (!string.Equals(configuredAuthorityTenant, "organizations", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("AzureAd:TenantId must be organizations in Organizations mode.");
            }

            if (allowedTenantIds.Count == 0)
            {
                failures.Add("Organizations mode requires at least one allowed tenant GUID.");
            }
        }

        var hasClientSecret = !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientSecret"]);
        var hasClientCredential = configuration.GetSection("AzureAd:ClientCredentials").GetChildren().Any()
            || configuration.GetSection("AzureAd:ClientCertificates").GetChildren().Any();
        if (environmentName.Equals(Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            if (!hasClientSecret && !hasClientCredential)
            {
                failures.Add("Development requires AzureAd:ClientSecret from user secrets or the environment, or a configured client credential.");
            }
        }
        else
        {
            if (hasClientSecret)
            {
                failures.Add("AzureAd:ClientSecret is permitted only in Development.");
            }

            if (!hasClientCredential)
            {
                failures.Add("Non-development environments require a certificate or managed-identity-backed client credential.");
            }
        }

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(BffAuthenticationSettings),
                typeof(BffAuthenticationSettings),
                failures);
        }

        return new BffAuthenticationSettings(
            mode,
            NormalizeInstance(instance),
            clientId,
            singleTenantId,
            allowedTenantIds);
    }

    private static Uri NormalizeInstance(Uri instance) =>
        new(instance.AbsoluteUri.TrimEnd('/') + "/");
}

internal sealed class AuthenticationConfigurationStartupValidator(
    BffAuthenticationSettings settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = settings;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
