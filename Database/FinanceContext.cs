using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using ExpenseTracker.Models;
using System;

namespace ExpenseTracker.Database
{
    public class FinanceContext
    {
        private readonly IMongoDatabase _database;

        public IMongoCollection<User> Users => _database.GetCollection<User>("tbl_users");
        public IMongoCollection<Expense> Expenses => _database.GetCollection<Expense>("tbl_expenses");
        public IMongoCollection<Budget> Budgets => _database.GetCollection<Budget>("tbl_budgets");
        public IMongoCollection<Category> Categories => _database.GetCollection<Category>("tbl_categories");

        public FinanceContext(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDBSettings:ConnectionString"];
            var databaseName = configuration["MongoDBSettings:DatabaseName"];

            Console.WriteLine($"✅ Connecting to MongoDB: {databaseName}");

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }
    }
}
