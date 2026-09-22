using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Transforms;

namespace Croesus.BffYarp.Security;

/// <summary>
/// The credential boundary applied to every proxied request.
/// <para>
/// Two things happen here and the order matters. First every credential and every spoofable identity or
/// forwarding header the browser supplied is removed, so nothing the caller controls survives the hop.
/// Then, and only then, the token the server acquired for this route's resource is attached.
/// </para>
/// <para>
/// Acquisition itself runs in <see cref="ProxyBoundaryMiddleware"/> rather than here. A transform cannot
/// fail a request cleanly: YARP turns a transform exception into a generic gateway error, which would hide
/// an interaction-required condition behind a 502. Acquiring first lets the boundary answer with a bounded
/// 401 and never forward at all. The token still comes from <c>ITokenAcquisition</c>; it is never read out
/// of cookie authentication properties.
/// </para>
/// </summary>
internal sealed class AccessTokenTransform : RequestTransform
{
    internal const string TokenItemKey = "Croesus.BffYarp.DownstreamAccessToken";

    /// <summary>Headers removed outright. Anything a caller could use to impersonate belongs on this list.</summary>
    private static readonly string[] StrippedHeaders =
    [
        HeaderNames.Cookie,
        HeaderNames.Authorization,
        "Proxy-Authorization",
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Proto",
        "X-Forwarded-Host",
        "X-Forwarded-Prefix",
        "X-Real-IP",
        "X-Original-URL",
        "X-Original-Host",
        "X-Rewrite-URL",
        "X-Http-Method-Override",
        "X-Method-Override"
    ];

    /// <summary>Header name prefixes that carry platform or application identity assertions.</summary>
    private static readonly string[] StrippedPrefixes =
    [
        "X-Ms-Client-Principal",
        "X-Ms-Token",
        "X-Croesus-Identity"
    ];

    public override ValueTask ApplyAsync(RequestTransformContext context)
    {
        foreach (var header in StrippedHeaders)
        {
            RemoveHeader(context, header);
        }

        foreach (var header in context.HttpContext.Request.Headers.Keys)
        {
            if (StrippedPrefixes.Any(prefix => header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                RemoveHeader(context, header);
            }
        }

        if (context.HttpContext.Items[TokenItemKey] is not string token || string.IsNullOrWhiteSpace(token))
        {
            // Unreachable through the guarded pipeline. If it is ever reached, refuse rather than forward
            // an anonymous request that the owned API might treat as a valid unauthenticated call.
            throw new InvalidOperationException(
                "No server-acquired access token was present for a proxied request.");
        }

        context.ProxyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return ValueTask.CompletedTask;
    }
}
