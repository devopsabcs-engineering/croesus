using Microsoft.Extensions.Options;

namespace Croesus.BffYarp.Security;

internal enum AuthorityMode
{
    SingleTenant,
    Organizations
}

/// <summary>
/// Validated startup configuration for the reference BFF. Loading fails fast rather than allowing the
/// application to start in a posture that silently weakens token custody or session isolation.
/// </summary>
internal sealed class BffAuthenticationSettings
{
    private BffAuthenticationSettings(
        AuthorityMode mode,
        Uri instance,
        Guid clientId,
        Guid? singleTenantId,
        IReadOnlySet<Guid> allowedTenantIds,
        IReadOnlyList<string> downstreamScopes,
        TimeSpan sessionAbsoluteExpiry,
        TimeSpan sessionIdleExpiry,
        bool hasSharedTicketStore,
        bool hasPersistedDataProtectionKeys)
    {
        Mode = mode;
        Instance = instance;
        ClientId = clientId;
        SingleTenantId = singleTenantId;
        AllowedTenantIds = allowedTenantIds;
        DownstreamScopes = downstreamScopes;
        SessionAbsoluteExpiry = sessionAbsoluteExpiry;
        SessionIdleExpiry = sessionIdleExpiry;
        HasSharedTicketStore = hasSharedTicketStore;
        HasPersistedDataProtectionKeys = hasPersistedDataProtectionKeys;
    }

    public AuthorityMode Mode { get; }

    public Uri Instance { get; }

    public Guid ClientId { get; }

    public Guid? SingleTenantId { get; }

    public IReadOnlySet<Guid> AllowedTenantIds { get; }

    /// <summary>Delegated scopes for the owned downstream API. Never a Microsoft Graph scope.</summary>
    public IReadOnlyList<string> DownstreamScopes { get; }

    public TimeSpan SessionAbsoluteExpiry { get; }

    public TimeSpan SessionIdleExpiry { get; }

    /// <summary>True when a backing store shared across instances and restarts is configured.</summary>
    public bool HasSharedTicketStore { get; }

    public bool HasPersistedDataProtectionKeys { get; }

    public static bool PermitsClientSecret(string environmentName) =>
        environmentName.Equals(Environments.Development, StringComparison.OrdinalIgnoreCase)
        || environmentName.Equals("Poc", StringComparison.OrdinalIgnoreCase);

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

        if (!Guid.TryParse(configuration["AzureAd:ClientId"], out var clientId) || clientId == Guid.Empty)
        {
            failures.Add("AzureAd:ClientId must be a non-empty GUID.");
        }

        var allowedTenantIds = new HashSet<Guid>();
        foreach (var configuredTenantId in configuration
                     .GetSection("Authentication:AllowedTenantIds")
                     .Get<string[]>()
                 ?? [])
        {
            if (!Guid.TryParse(configuredTenantId, out var allowedTenantId) || allowedTenantId == Guid.Empty)
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
            if (!Guid.TryParse(configuredAuthorityTenant, out var parsedTenantId) || parsedTenantId == Guid.Empty)
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

        var downstreamScopes = configuration.GetSection("DownstreamApi:Scopes").Get<string[]>() ?? [];
        if (downstreamScopes.Length == 0)
        {
            failures.Add("DownstreamApi:Scopes must list at least one delegated scope for the owned API.");
        }

        foreach (var scope in downstreamScopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                failures.Add("DownstreamApi:Scopes entries must be non-empty.");
            }
            else if (scope.Contains("graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("DownstreamApi:Scopes must not request Microsoft Graph; the BFF calls only the owned API.");
            }
        }

        var absoluteExpiry = ReadPositiveMinutes(
            configuration, "Session:AbsoluteExpiryMinutes", defaultMinutes: 60, failures);
        var idleExpiry = ReadPositiveMinutes(
            configuration, "Session:IdleExpiryMinutes", defaultMinutes: 20, failures);
        if (idleExpiry > absoluteExpiry)
        {
            failures.Add("Session:IdleExpiryMinutes must not exceed Session:AbsoluteExpiryMinutes.");
        }

        var hasSharedTicketStore = !string.IsNullOrWhiteSpace(configuration["DistributedCache:Redis:ConnectionString"]);
        var hasPersistedKeys = !string.IsNullOrWhiteSpace(configuration["DataProtection:KeyRingPath"]);

        var hasClientSecret = !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientSecret"]);
        var hasClientCredential = configuration.GetSection("AzureAd:ClientCredentials").GetChildren().Any()
            || configuration.GetSection("AzureAd:ClientCertificates").GetChildren().Any();
        if (PermitsClientSecret(environmentName))
        {
            if (!hasClientSecret && !hasClientCredential)
            {
                failures.Add("Development and Poc require AzureAd:ClientSecret from secure configuration, or a configured client credential.");
            }
        }
        else
        {
            if (hasClientSecret)
            {
                failures.Add("AzureAd:ClientSecret is permitted only in Development or Poc.");
            }

            if (!hasClientCredential)
            {
                failures.Add("Environments other than Development or Poc require a certificate or managed-identity-backed client credential.");
            }

            // A process-local cache cannot hold a session across a restart or a second instance, and an
            // ephemeral key ring cannot decrypt a ticket another instance wrote. Both are fail-closed here.
            if (!hasSharedTicketStore)
            {
                failures.Add("Environments other than Development or Poc require DistributedCache:Redis:ConnectionString so the ticket and token caches survive restart and span instances.");
            }

            if (!hasPersistedKeys)
            {
                failures.Add("Environments other than Development or Poc require DataProtection:KeyRingPath so Data Protection keys are persisted and protected.");
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
            allowedTenantIds,
            downstreamScopes,
            absoluteExpiry,
            idleExpiry,
            hasSharedTicketStore,
            hasPersistedKeys);
    }

    private static TimeSpan ReadPositiveMinutes(
        IConfiguration configuration,
        string key,
        int defaultMinutes,
        List<string> failures)
    {
        var text = configuration[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return TimeSpan.FromMinutes(defaultMinutes);
        }

        if (!int.TryParse(text, out var minutes) || minutes <= 0)
        {
            failures.Add($"{key} must be a positive whole number of minutes.");
            return TimeSpan.FromMinutes(defaultMinutes);
        }

        return TimeSpan.FromMinutes(minutes);
    }

    private static Uri NormalizeInstance(Uri instance) =>
        new(instance.AbsoluteUri.TrimEnd('/') + "/");
}

internal sealed class AuthenticationConfigurationStartupValidator(
    BffAuthenticationSettings settings,
    ProxyDestinationPolicy destinationPolicy,
    ForwardedHeadersPolicy forwardedHeadersPolicy) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = settings;
        _ = destinationPolicy;
        _ = forwardedHeadersPolicy;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
