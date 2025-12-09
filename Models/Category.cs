using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ExpenseTracker.Models
{
    [BsonIgnoreExtraElements]
    public class Category
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("category_id")]
        public string? CategoryId { get; set; }

        [BsonElement("category_name")]
        public string CategoryName { get; set; } = string.Empty;

        // ✅ Backwards compatibility
        [BsonIgnore]
        public string Name
        {
            get => CategoryName;
            set => CategoryName = value;
        }
    }
}