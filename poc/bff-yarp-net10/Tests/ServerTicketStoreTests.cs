using System.Security.Claims;
using Croesus.BffYarp.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Step 3.2 evidence at the unit level: key opacity, restart survival, and fail-closed behaviour when the
/// backing store is unavailable.
/// </summary>
public sealed class ServerTicketStoreTests
{
    [Fact]
    public async Task StoreAsync_ReturnsAnOpaqueKeyThatLeaksNothingFromTheTicket()
    {
        var store = CreateStore(out _);

        var key = await store.StoreAsync(CreateTicket());

        Assert.StartsWith(ServerTicketStore.KeyPrefix, key, StringComparison.Ordinal);
        Assert.DoesNotContain(TestConfiguration.SentinelSecret, key, StringComparison.Ordinal);
        Assert.DoesNotContain(TestConfiguration.ObjectId, key, StringComparison.Ordinal);

        var second = await store.StoreAsync(CreateTicket());
        Assert.NotEqual(key, second);
    }

    [Fact]
    public async Task RetrieveAsync_AfterAProcessRestart_StillResolvesTheSession()
    {
        var cache = NewCache();
        var original = CreateStore(cache);
        var key = await original.StoreAsync(CreateTicket());

        // A second store instance over the same backing store is what a restarted process looks like.
        var restarted = CreateStore(cache);
        var recovered = await restarted.RetrieveAsync(key);

        Assert.NotNull(recovered);
        Assert.Equal(TestConfiguration.ObjectId, recovered.Principal.FindFirstValue("oid"));
        Assert.Equal(TestConfiguration.SentinelSecret, recovered.Properties.GetTokenValue("access_token"));
    }

    [Fact]
    public async Task RetrieveAsync_WhenTheStoreIsUnavailable_FailsClosed()
    {
        var store = CreateStore(new UnavailableDistributedCache());

        var recovered = await store.RetrieveAsync(ServerTicketStore.KeyPrefix + "anything");

        Assert.Null(recovered);
    }

    [Fact]
    public async Task RetrieveAsync_WithAKeyThatDidNotComeFromThisStore_ReturnsNothing()
    {
        var store = CreateStore(out _);

        Assert.Null(await store.RetrieveAsync("not-a-store-key"));
        Assert.Null(await store.RetrieveAsync(string.Empty));
    }

    [Fact]
    public async Task RemoveAsync_MakesTheSessionUnresolvable()
    {
        var cache = NewCache();
        var store = CreateStore(cache);
        var key = await store.StoreAsync(CreateTicket());

        await store.RemoveAsync(key);

        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task StoreAsync_BoundsTheEntryByBothAbsoluteAndIdleExpiry()
    {
        var cache = new RecordingDistributedCache();
        var store = CreateStore(cache);

        await store.StoreAsync(CreateTicket());

        Assert.NotNull(cache.LastOptions);
        Assert.NotNull(cache.LastOptions.AbsoluteExpiration);
        Assert.Equal(TimeSpan.FromMinutes(20), cache.LastOptions.SlidingExpiration);
    }

    private static AuthenticationTicket CreateTicket()
    {
        var identity = new ClaimsIdentity(
            [new Claim("oid", TestConfiguration.ObjectId), new Claim("tid", TestConfiguration.TenantId)],
            "TestOidc");
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = TestConfiguration.SentinelSecret }]);
        return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, "Cookies");
    }

    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static ServerTicketStore CreateStore(out IDistributedCache cache)
    {
        cache = NewCache();
        return CreateStore(cache);
    }

    private static ServerTicketStore CreateStore(IDistributedCache cache)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(TestConfiguration.Values())
            .Build();
        var settings = BffAuthenticationSettings.Load(configuration, "Development");
        return new ServerTicketStore(cache, settings, TimeProvider.System, NullLogger<ServerTicketStore>.Instance);
    }

    private sealed class UnavailableDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Store unavailable.");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Store unavailable.");

        public void Refresh(string key) => throw new InvalidOperationException("Store unavailable.");

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Store unavailable.");

        public void Remove(string key) => throw new InvalidOperationException("Store unavailable.");

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("Store unavailable.");

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("Store unavailable.");

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("Store unavailable.");
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly IDistributedCache _inner = NewCache();

        public DistributedCacheEntryOptions? LastOptions { get; private set; }

        public byte[]? Get(string key) => _inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public void Remove(string key) => _inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            LastOptions = options;
            _inner.Set(key, value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            LastOptions = options;
            return _inner.SetAsync(key, value, options, token);
        }
    }
}
