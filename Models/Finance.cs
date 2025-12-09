using System.Collections.Generic;

namespace ExpenseTracker.Models
{
    public class Finance
    {
        public List<Expense> Expenses { get; set; } = new();
        public List<Budget> Budgets { get; set; } = new();
    }
}
