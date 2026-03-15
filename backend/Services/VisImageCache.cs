using StackExchange.Redis;

namespace Bakery.Api.Services;

public interface IVisImageCache
{
    Task<string> StoreAsync(byte[] bytes, int ttlSeconds);
    Task<byte[]?> GetAsync(string token);
}

public class VisImageCache : IVisImageCache
{
    private readonly IDatabase _db;

    public VisImageCache(IConnectionMultiplexer mux)
    {
        _db = mux.GetDatabase();
    }

    public async Task<string> StoreAsync(byte[] bytes, int ttlSeconds)
    {
        var token = Guid.NewGuid().ToString("N");
        var key = $"visimg:{token}";
        await _db.StringSetAsync(key, bytes, TimeSpan.FromSeconds(ttlSeconds));
        return token;
    }

    public async Task<byte[]?> GetAsync(string token)
    {
        var key = $"visimg:{token}";
        var val = await _db.StringGetAsync(key);
        return val.HasValue ? (byte[])val! : null;
    }
}
