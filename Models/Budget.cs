using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ExpenseTracker.Models
{
    [BsonIgnoreExtraElements]
    public class Budget
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("budget_id")]
        public string? BudgetId { get; set; }

        [BsonElement("user_id")]
        public string UserId { get; set; } = string.Empty;

        [BsonElement("category_id")]
        public string CategoryId { get; set; } = string.Empty;

        [BsonElement("budget_amount")]
        public decimal BudgetAmount { get; set; }

        // ✅ REMOVED from database - now calculated dynamically
        // [BsonElement("spent_amount")]
        // public decimal SpentAmount { get; set; } = 0;

        [BsonElement("month")]
        public int Month { get; set; }

        [BsonElement("year")]
        public int Year { get; set; }

        [BsonElement("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ✅ SpentAmount is now ONLY a helper property (calculated, not stored)
        [BsonIgnore]
        public decimal SpentAmount { get; set; } = 0;

        // ✅ Helper properties (not stored in DB)
        [BsonIgnore]
        public decimal RemainingAmount => BudgetAmount - SpentAmount;

        [BsonIgnore]
        public string CategoryName { get; set; } = string.Empty;

        [BsonIgnore]
        public double PercentageUsed => BudgetAmount > 0 ? (double)(SpentAmount / BudgetAmount * 100) : 0;

        // ✅ Backwards compatibility
        [BsonIgnore]
        public string Category
        {
            get => CategoryId;
            set => CategoryId = value;
        }

        [BsonIgnore]
        public decimal Amount
        {
            get => BudgetAmount;
            set => BudgetAmount = value;
        }

        [BsonIgnore]
        public string? Description { get; set; }

        [BsonIgnore]
        public string? CustomCategory { get; set; }
    }
}