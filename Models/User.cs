using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpenseTracker.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("user_id")]
        public string? UserId { get; set; }

        [BsonElement("first_name")]
        public string FirstName { get; set; } = "";

        [BsonElement("middle_name")]
        [BsonIgnoreIfDefault]
        public string? MiddleName { get; set; }

        [BsonElement("middle_initial")]
        [BsonIgnoreIfDefault]
        public string? MiddleInitial { get; set; }

        [BsonElement("last_name")]
        public string LastName { get; set; } = "";

        // ✅ CHANGED: Back to List for compatibility with existing database
        [BsonElement("email")]
        public List<string> Email { get; set; } = new List<string>();

        [BsonElement("phone_number")]
        public List<string> PhoneNumber { get; set; } = new List<string>();

        [BsonElement("address_street")]
        public string AddressStreet { get; set; } = "";

        [BsonElement("address_city")]
        public string AddressCity { get; set; } = "";

        [BsonElement("address_province")]
        public string AddressProvince { get; set; } = "";

        [BsonElement("address_postal_code")]
        public string AddressPostalCode { get; set; } = "";

        [BsonElement("username")]
        public string Username { get; set; } = "";

        [BsonElement("password")]
        public string Password { get; set; } = "";

        [BsonIgnore]
        public string FullName
        {
            get
            {
                var middle = !string.IsNullOrEmpty(MiddleName) ? MiddleName : MiddleInitial ?? "";
                return $"{FirstName} {middle} {LastName}".Trim();
            }
        }

        // ✅ UNCHANGED: These still work the same for forms
        [BsonIgnore]
        public string PrimaryEmail
        {
            get => Email.FirstOrDefault() ?? "";
            set { if (!string.IsNullOrEmpty(value)) Email = new List<string> { value }; }
        }

        [BsonIgnore]
        public string PrimaryPhoneNumber
        {
            get => PhoneNumber.FirstOrDefault() ?? "";
            set { if (!string.IsNullOrEmpty(value)) PhoneNumber = new List<string> { value }; }
        }

        [BsonIgnore]
        public string ConfirmPassword { get; set; } = "";
    }
}