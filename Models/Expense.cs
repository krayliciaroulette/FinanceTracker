using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace ExpenseTracker.Models
{
    [BsonIgnoreExtraElements]
    public class Expense
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("expense_id")]
        public string? ExpenseId { get; set; }

        [BsonElement("user_id")]
        public string UserId { get; set; } = string.Empty;

        [BsonElement("category_id")]
        public string CategoryId { get; set; } = string.Empty;

        [BsonElement("custom_category_name")]
        public string? CustomCategoryName { get; set; }

        [BsonElement("amount")]
        public decimal Amount { get; set; }

        [BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        [BsonElement("expense_date")]
        public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

        [BsonElement("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ✅ Track if expense exceeds budget
        [BsonElement("is_over_budget")]
        public bool IsOverBudget { get; set; } = false;

        // ✅ NEW: Link split expenses together (expenses created from same transaction)
        [BsonElement("transaction_group_id")]
        public string? TransactionGroupId { get; set; }

        // ✅ Helper properties (not stored in DB)
        [BsonIgnore]
        public string CategoryName { get; set; } = string.Empty;

        [BsonIgnore]
        public string DisplayCategoryName => !string.IsNullOrEmpty(CustomCategoryName) ? CustomCategoryName : CategoryName;

        // ✅ Backwards compatibility for old views
        [BsonIgnore]
        public string Category
        {
            get => CategoryId;
            set => CategoryId = value;
        }

        [BsonIgnore]
        public DateTime Date
        {
            get => ExpenseDate;
            set => ExpenseDate = value;
        }
    }
}