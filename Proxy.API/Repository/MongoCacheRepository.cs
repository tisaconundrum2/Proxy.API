using System.Threading.Tasks;
using System;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using Microsoft.Extensions.Options;
using Proxy.API.Models;

public class MongoCacheRepository
{
    private readonly IMongoCollection<CachedResponse> _collection;

    public MongoCacheRepository(IMongoClient client, IOptions<MongoSettings> settings)
    {
        var mongoSettings = settings.Value;
        var database = client.GetDatabase(mongoSettings.DatabaseName);
        _collection = database.GetCollection<CachedResponse>(mongoSettings.CollectionName);
    }

    public async Task<CachedResponse> GetCacheAsync(string hash)
    {
        var filter = Builders<CachedResponse>.Filter.And(
            Builders<CachedResponse>.Filter.Eq(x => x.Hash, hash),
            Builders<CachedResponse>.Filter.Gt(x => x.ExpirationTime, DateTime.UtcNow)
        );
        return await _collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task SetCacheAsync(CachedResponse cache)
    {
        if (string.IsNullOrEmpty(cache.Id))
        {
            cache.Id = ObjectId.GenerateNewId().ToString(); // Generate a new ObjectId if Id is null or empty
        }

        var filter = Builders<CachedResponse>.Filter.Eq(x => x.Hash, cache.Hash);
        var update = Builders<CachedResponse>.Update
            .Set(x => x.Hash, cache.Hash)
            .Set(x => x.Url, cache.Url)
            .Set(x => x.Content, cache.Content)
            .Set(x => x.ContentType, cache.ContentType)
            .Set(x => x.ExpirationTime, cache.ExpirationTime)
            .Set(x => x.CreatedAt, cache.CreatedAt);

        await _collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
    }
}