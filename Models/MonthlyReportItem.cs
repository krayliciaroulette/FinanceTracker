namespace ExpenseTracker.Models
{
    public class MonthlyReportItem
    {
        public int Month { get; set; }
        public int Year { get; set; }
        public decimal TotalExpenses { get; set; }
        public int Transactions { get; set; }
    }
}
