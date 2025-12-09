using ExpenseTracker.Models;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace ExpenseTracker.Database
{
    public class MongoDbService
    {
        public readonly IMongoDatabase Database;
        public readonly IMongoCollection<User> Users;
        public readonly IMongoCollection<Expense> Expenses;
        public readonly IMongoCollection<Budget> Budgets;

        public MongoDbService(IConfiguration configuration)
        {
            // 1️⃣ Read from environment variables (Render)
            var connectionString = Environment.GetEnvironmentVariable("MONGO_URI");
            var dbName = Environment.GetEnvironmentVariable("MONGO_DB");

            // 2️⃣ If environment variables not set, read from appsettings.json
            connectionString ??= configuration["MongoDBSettings:ConnectionString"];
            dbName ??= configuration["MongoDBSettings:DatabaseName"];

            // 3️⃣ No local fallback (since you only use Atlas)
            if (string.IsNullOrEmpty(connectionString))
                throw new Exception("❌ MongoDB connection string is missing.");

            if (string.IsNullOrEmpty(dbName))
                throw new Exception("❌ MongoDB database name is missing.");

            var client = new MongoClient(connectionString);
            Database = client.GetDatabase(dbName);

            Users = Database.GetCollection<User>("Users");
            Expenses = Database.GetCollection<Expense>("Expenses");
            Budgets = Database.GetCollection<Budget>("Budgets");

            Console.WriteLine($"✅ Connected to MongoDB Atlas database: {dbName}");
        }
    }
}
