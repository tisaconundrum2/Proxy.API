using System.Threading.Tasks;
using System;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using Microsoft.Extensions.Options;

public class MongoCacheRepository
{
    private readonly IMongoCollection<CachedResponse> _collection;

    public MongoCacheRepository(IMongoClient client, IOptions<MongoSettings> settings)
    {
        var mongoSettings = settings.Value;
        var database = client.GetDatabase(mongoSettings.DatabaseName);
        _collection = database.GetCollection<CachedResponse>(mongoSettings.CollectionName);
    }

    public async Task<CachedResponse> GetCacheAsync(string url)
    {
        var filter = Builders<CachedResponse>.Filter.And(
            Builders<CachedResponse>.Filter.Eq(x => x.Url, url),
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

        var filter = Builders<CachedResponse>.Filter.Eq(x => x.Url, cache.Url);
        var update = Builders<CachedResponse>.Update
            .Set(x => x.Url, cache.Url)
            .Set(x => x.Content, cache.Content)
            .Set(x => x.ContentType, cache.ContentType)
            .Set(x => x.ExpirationTime, cache.ExpirationTime);

        await _collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
    }
}

public class CachedResponse
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }

    [BsonElement("url")]
    public string Url { get; set; }

    [BsonElement("content")]
    public string Content { get; set; }

    [BsonElement("contentType")]
    public string ContentType { get; set; }

    [BsonElement("expirationTime")]
    public DateTime ExpirationTime { get; set; }
}
