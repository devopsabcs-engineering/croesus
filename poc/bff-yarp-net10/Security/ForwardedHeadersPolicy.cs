using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace Croesus.BffYarp.Security;

/// <summary>
/// Forwarded-header trust boundary, re-derived for the private-ingress topology rather than inherited.
/// <para>
/// The modern reference app enables <c>UseForwardedHeaders</c> only when a non-empty <c>KnownProxies</c>
/// list is configured. That guard is kept, because it is the control that prevents a client from forging
/// its own <c>X-Forwarded-For</c>. Three things are added on top of it, because a private endpoint changes
/// both the trusted peer and the measured hop count:
/// </para>
/// <list type="number">
/// <item>The default loopback trust that <see cref="ForwardedHeadersOptions"/> ships with is cleared. The
/// deployed peer is a platform front end reached through a private endpoint, not localhost, so leaving the
/// loopback default in place would trust a hop that is not on the measured path.</item>
/// <item><c>ForwardLimit</c> must be stated explicitly and matched to the measured hop count. Raising it to
/// absorb an unexpected header is the failure mode this guard exists to prevent.</item>
/// <item>Trust is expressed as specific peer addresses or specific networks. A blanket
/// <c>KnownNetworks.Add(0.0.0.0/0)</c> equivalent is rejected at load time.</item>
/// </list>
/// <para>
/// Until the private topology is provisioned and the hop count is measured against it, the configured trust
/// set is empty, <see cref="IsEnabled"/> is false, and the middleware is never added to the pipeline. That is
/// the fail-closed default: no forwarded header is honoured at all.
/// </para>
/// </summary>
internal sealed class ForwardedHeadersPolicy
{
    private ForwardedHeadersPolicy(
        IReadOnlyList<IPAddress> knownProxies,
        IReadOnlyList<IPNetwork> knownNetworks,
        int forwardLimit)
    {
        KnownProxies = knownProxies;
        KnownNetworks = knownNetworks;
        ForwardLimit = forwardLimit;
    }

    public IReadOnlyList<IPAddress> KnownProxies { get; }

    public IReadOnlyList<IPNetwork> KnownNetworks { get; }

    public int ForwardLimit { get; }

    /// <summary>
    /// False until at least one peer or network has been measured against the deployed topology. While false
    /// the forwarded-headers middleware is not registered and no client-supplied forwarding header is honoured.
    /// </summary>
    public bool IsEnabled => KnownProxies.Count > 0 || KnownNetworks.Count > 0;

    public static ForwardedHeadersPolicy Load(IConfiguration configuration)
    {
        var failures = new List<string>();
        var section = configuration.GetSection("ForwardedHeaders");

        var proxies = new List<IPAddress>();
        foreach (var candidate in section.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(candidate, out var address))
            {
                failures.Add($"ForwardedHeaders:KnownProxies entry '{candidate}' is not an IP address.");
                continue;
            }

            proxies.Add(address);
        }

        var networks = new List<IPNetwork>();
        foreach (var candidate in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            if (!IPNetwork.TryParse(candidate, out var network))
            {
                failures.Add($"ForwardedHeaders:KnownNetworks entry '{candidate}' is not CIDR notation.");
                continue;
            }

            if (network.PrefixLength == 0)
            {
                failures.Add($"ForwardedHeaders:KnownNetworks entry '{candidate}' trusts every peer and is rejected.");
                continue;
            }

            networks.Add(network);
        }

        var forwardLimitText = section["ForwardLimit"];
        var forwardLimit = 1;
        if (!string.IsNullOrWhiteSpace(forwardLimitText))
        {
            if (!int.TryParse(forwardLimitText, out forwardLimit) || forwardLimit < 1)
            {
                failures.Add("ForwardedHeaders:ForwardLimit must be a whole number of measured hops, at least 1.");
                forwardLimit = 1;
            }
        }

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(ForwardedHeadersPolicy),
                typeof(ForwardedHeadersPolicy),
                failures);
        }

        return new ForwardedHeadersPolicy(proxies, networks, forwardLimit);
    }

    public void Apply(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = ForwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in KnownProxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in KnownNetworks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }
}
