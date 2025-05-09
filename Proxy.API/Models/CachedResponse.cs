using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Proxy.API.Models
{
    public class CachedResponse
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("hash")]
        public required string Hash { get; set; }

        [BsonElement("url")]
        public required string Url { get; set; }

        [BsonElement("content")]
        public required string Content { get; set; }

        [BsonElement("contentType")]
        public required string ContentType { get; set; }

        [BsonElement("expirationTime")]
        public DateTime ExpirationTime { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}