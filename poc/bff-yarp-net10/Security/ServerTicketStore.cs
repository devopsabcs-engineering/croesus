using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Distributed;

namespace Croesus.BffYarp.Security;

/// <summary>
/// Server-side custody of the authentication ticket.
/// <para>
/// This is the control that makes the browser hold a reference and nothing else. The cookie carries only the
/// opaque key produced here; the serialized ticket, including any claims and any authentication properties,
/// stays in the distributed cache. Setting <c>SaveTokens = true</c> would instead place tokens inside the
/// browser-held encrypted ticket, which is the design defect this store exists to remove.
/// </para>
/// <para>
/// Retrieval fails closed. A cache outage, a deserialization failure, or a missing entry all resolve to a
/// null ticket, which the cookie handler treats as no authenticated session. No path falls back to a
/// browser-supplied token or to another entry.
/// </para>
/// </summary>
internal sealed class ServerTicketStore(
    IDistributedCache cache,
    BffAuthenticationSettings settings,
    TimeProvider timeProvider,
    ILogger<ServerTicketStore> logger) : ITicketStore
{
    internal const string KeyPrefix = "croesus-bff-ticket:";
    private const int KeyByteLength = 32;

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var key = KeyPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(KeyByteLength));
        await WriteAsync(key, ticket).ConfigureAwait(false);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(ticket);
        return WriteAsync(key, ticket);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        byte[]? payload;
        try
        {
            payload = await cache.GetAsync(key).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Fail closed: an unreachable store means no session, never an assumed one.
            logger.LogWarning(
                "Ticket store read failed ({ExceptionType}); the request is treated as unauthenticated.",
                exception.GetType().Name);
            return null;
        }

        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(payload);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Ticket store payload could not be deserialized ({ExceptionType}); the request is treated as unauthenticated.",
                exception.GetType().Name);
            return null;
        }
    }

    public async Task RemoveAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        try
        {
            await cache.RemoveAsync(key).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Surfacing the failure would leak store state to the caller; the caller still loses its cookie.
            logger.LogError(
                "Ticket store removal failed ({ExceptionType}); the entry expires on its absolute bound instead.",
                exception.GetType().Name);
        }
    }

    private Task WriteAsync(string key, AuthenticationTicket ticket)
    {
        var now = timeProvider.GetUtcNow();
        var absolute = ticket.Properties.ExpiresUtc is { } expires && expires <= now + settings.SessionAbsoluteExpiry
            ? expires
            : now + settings.SessionAbsoluteExpiry;

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = absolute,
            SlidingExpiration = settings.SessionIdleExpiry
        };

        return cache.SetAsync(key, TicketSerializer.Default.Serialize(ticket), options);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
