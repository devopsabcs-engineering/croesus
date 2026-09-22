using Microsoft.Extensions.Options;

namespace Croesus.BffYarp.Security;

/// <summary>
/// Startup validation of the proxy boundary. Every destination the reverse proxy may reach is fixed in
/// configuration, validated here before the first request, and checked against an explicit origin allowlist.
/// Nothing in a request can introduce, rewrite, or widen a destination.
/// </summary>
internal sealed class ProxyDestinationPolicy
{
    private ProxyDestinationPolicy(
        IReadOnlySet<string> allowedOrigins,
        IReadOnlyList<string> routePrefixes,
        IReadOnlyDictionary<string, Uri> destinations)
    {
        AllowedOrigins = allowedOrigins;
        RoutePrefixes = routePrefixes;
        Destinations = destinations;
    }

    public IReadOnlySet<string> AllowedOrigins { get; }

    /// <summary>Path prefixes the proxy boundary guards. A request outside them is never forwarded.</summary>
    public IReadOnlyList<string> RoutePrefixes { get; }

    /// <summary>Configured destinations, keyed by <c>cluster/destination</c>, after validation.</summary>
    public IReadOnlyDictionary<string, Uri> Destinations { get; }

    public bool IsGuardedPath(PathString path) =>
        RoutePrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    public bool IsAllowedOrigin(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && AllowedOrigins.Contains(NormalizeOrigin(uri));

    public static ProxyDestinationPolicy Load(IConfiguration configuration)
    {
        var failures = new List<string>();
        var policySection = configuration.GetSection("ProxyPolicy");

        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in policySection.GetSection("AllowedDestinationOrigins").Get<string[]>() ?? [])
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var origin)
                || origin.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(origin.Query)
                || !string.IsNullOrEmpty(origin.Fragment))
            {
                failures.Add($"ProxyPolicy:AllowedDestinationOrigins entry '{candidate}' must be an absolute HTTPS origin.");
                continue;
            }

            allowedOrigins.Add(NormalizeOrigin(origin));
        }

        var routePrefixes = new List<string>();
        foreach (var candidate in policySection.GetSection("RoutePrefixes").Get<string[]>() ?? [])
        {
            if (string.IsNullOrWhiteSpace(candidate) || !candidate.StartsWith('/'))
            {
                failures.Add($"ProxyPolicy:RoutePrefixes entry '{candidate}' must be a rooted path.");
                continue;
            }

            routePrefixes.Add(candidate.TrimEnd('/'));
        }

        var destinations = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var cluster in configuration.GetSection("ReverseProxy:Clusters").GetChildren())
        {
            foreach (var destination in cluster.GetSection("Destinations").GetChildren())
            {
                var address = destination["Address"];
                var key = $"{cluster.Key}/{destination.Key}";
                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                {
                    failures.Add($"Destination '{key}' must be an absolute HTTPS address.");
                    continue;
                }

                if (!allowedOrigins.Contains(NormalizeOrigin(uri)))
                {
                    failures.Add($"Destination '{key}' resolves to origin '{NormalizeOrigin(uri)}', which is not in ProxyPolicy:AllowedDestinationOrigins.");
                    continue;
                }

                destinations[key] = uri;
            }
        }

        foreach (var route in configuration.GetSection("ReverseProxy:Routes").GetChildren())
        {
            var path = route["Match:Path"];
            if (string.IsNullOrWhiteSpace(path))
            {
                failures.Add($"Route '{route.Key}' must declare Match:Path so the guarded prefix is explicit.");
                continue;
            }

            if (!routePrefixes.Any(prefix => path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"Route '{route.Key}' path '{path}' is outside ProxyPolicy:RoutePrefixes and would bypass the proxy boundary.");
            }
        }

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(ProxyDestinationPolicy),
                typeof(ProxyDestinationPolicy),
                failures);
        }

        return new ProxyDestinationPolicy(allowedOrigins, routePrefixes, destinations);
    }

    private static string NormalizeOrigin(Uri uri) =>
        $"{uri.Scheme}://{uri.Host}:{uri.Port}";
}
