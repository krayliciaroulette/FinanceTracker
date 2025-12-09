using ExpenseTracker.Database;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Drawing = System.Drawing;
using System.Runtime.InteropServices;

namespace ExpenseTracker.Controllers
{
    public class FinanceController : Controller
    {
        private readonly FinanceContext _context;

        public FinanceController(FinanceContext context)
        {
            _context = context;
        }

        // ✅ Dashboard — User-specific
        // ✅ Dashboard — User-specific
        // ✅ INDEX (Dashboard) - Calculate SpentAmount dynamically
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Singapore Standard Time" : "Asia/Manila"
            );
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);
            int currentMonth = now.Month;
            int currentYear = now.Year;

            var budgets = _context.Budgets.Find(b =>
                b.UserId == userId &&
                b.Month == currentMonth &&
                b.Year == currentYear
            ).ToList();

            var monthlyExpenses = _context.Expenses
                .Find(e => e.UserId == userId && e.ExpenseDate.Month == currentMonth && e.ExpenseDate.Year == currentYear)
                .ToList();

            var categories = _context.Categories.Find(_ => true).ToList();

            // ✅ Calculate SpentAmount for each budget dynamically - COUNT ALL EXPENSES
            foreach (var budget in budgets)
            {
                budget.CategoryName = categories.FirstOrDefault(c => c.CategoryId == budget.CategoryId)?.CategoryName ?? "Unknown";

                // ✅ Count ALL expenses for this category (both planned and unplanned)
                budget.SpentAmount = monthlyExpenses
                    .Where(e => e.CategoryId == budget.CategoryId)
                    .Sum(e => e.Amount);
            }

            decimal totalBudget = budgets.Sum(b => b.BudgetAmount);
            decimal totalSpent = budgets.Sum(b => b.SpentAmount);
            decimal remaining = totalBudget - totalSpent; // Can be negative

            var startOfDay = now.Date;
            var endOfDay = startOfDay.AddDays(1);

            var todayExpenses = _context.Expenses
                .Find(e => e.UserId == userId && e.ExpenseDate >= startOfDay && e.ExpenseDate < endOfDay)
                .ToList();

            decimal totalSpentToday = todayExpenses.Sum(e => e.Amount);
            decimal totalMonthlyExpenses = monthlyExpenses.Sum(e => e.Amount);

            foreach (var expense in todayExpenses.Concat(monthlyExpenses))
            {
                expense.CategoryName = categories.FirstOrDefault(c => c.CategoryId == expense.CategoryId)?.CategoryName ?? "Unknown";
            }

            var budgetChartData = budgets
                .Where(b => categories.Any(c => c.CategoryId == b.CategoryId))
                .Select(b => new
                {
                    CategoryName = b.CategoryName,
                    Spent = b.SpentAmount,
                    Remaining = b.RemainingAmount
                }).ToList();

            var budgetedCategoryIds = new HashSet<string>(budgets.Select(b => b.CategoryId));

            var categoryTotals = todayExpenses
                .Where(e => categories.Any(c => c.CategoryId == e.CategoryId))
                .GroupBy(e => e.DisplayCategoryName)
                .Select(g => new
                {
                    Category = g.Key,
                    Total = g.Sum(x => x.Amount),
                    CategoryId = g.First().CategoryId,
                    // ✅ IsPlanned = true if category has a budget (regardless of IsOverBudget flag)
                    IsPlanned = budgetedCategoryIds.Contains(g.First().CategoryId)
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            ViewBag.TotalBudgets = totalBudget;
            ViewBag.TotalExpenses = totalSpentToday;
            ViewBag.TotalMonthlyExpenses = totalMonthlyExpenses;
            ViewBag.Remaining = remaining;
            ViewBag.CurrentMonth = $"{now:MMMM yyyy}";
            ViewBag.TodayExpenses = todayExpenses;
            ViewBag.MonthlyExpenses = monthlyExpenses;
            ViewBag.BudgetChartData = System.Text.Json.JsonSerializer.Serialize(budgetChartData);
            ViewBag.PieChartData = System.Text.Json.JsonSerializer.Serialize(categoryTotals);

            return View("Index");
        }

        public IActionResult Expenses(DateTime? startDate, DateTime? endDate, string categoryId, decimal? minAmount, decimal? maxAmount)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var expenses = _context.Expenses.Find(e => e.UserId == userId).ToList();

            if (startDate.HasValue && endDate.HasValue)
            {
                DateTime endOfDay = endDate.Value.AddDays(1).AddTicks(-1);
                expenses = expenses.Where(e => e.ExpenseDate >= startDate.Value && e.ExpenseDate <= endOfDay).ToList();
            }

            if (!string.IsNullOrEmpty(categoryId))
                expenses = expenses.Where(e => e.CategoryId == categoryId).ToList();

            if (minAmount.HasValue)
                expenses = expenses.Where(e => e.Amount >= minAmount.Value).ToList();
            if (maxAmount.HasValue)
                expenses = expenses.Where(e => e.Amount <= maxAmount.Value).ToList();

            var categories = _context.Categories.Find(_ => true).ToList();
            foreach (var expense in expenses)
            {
                expense.CategoryName = categories.FirstOrDefault(c => c.CategoryId == expense.CategoryId)?.CategoryName ?? "Unknown";
            }

            ViewBag.Categories = categories;

            expenses = expenses.OrderByDescending(e => e.ExpenseDate).ToList();
            return View(expenses);
        }

        [HttpGet]
        public IActionResult CreateExpense()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var today = DateTime.UtcNow.AddHours(8).Date;

            var todaysExpenses = _context.Expenses
                .Find(e => e.UserId == userId && e.ExpenseDate >= today && e.ExpenseDate < today.AddDays(1))
                .ToList();

            var categories = _context.Categories.Find(_ => true).ToList();
            ViewBag.Categories = categories;

            ViewBag.Today = today.ToString("MMMM dd, yyyy");
            ViewBag.TodaysExpenses = todaysExpenses;
            ViewBag.TotalToday = todaysExpenses.Sum(e => e.Amount);

            return View(new Expense());
        }
        [HttpPost]
        public IActionResult CreateExpense(Expense expense, string? customCategoryName)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            expense.UserId = userId;

            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Singapore Standard Time" : "Asia/Manila"
            );
            expense.ExpenseDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);
            expense.CreatedAt = DateTime.UtcNow;

            // Get next expense ID
            var allExpenses = _context.Expenses.Find(_ => true).ToList();
            int newId = 1;
            if (allExpenses.Any())
            {
                var validIds = allExpenses
                    .Where(e => !string.IsNullOrEmpty(e.ExpenseId) && int.TryParse(e.ExpenseId, out _))
                    .Select(e => int.Parse(e.ExpenseId!))
                    .ToList();
                if (validIds.Any())
                    newId = validIds.Max() + 1;
            }

            // CREATE UNIQUE TRANSACTION GROUP ID for this expense entry
            var transactionGroupId = Guid.NewGuid().ToString();

            // CHECK BUDGET AND SPLIT IF NEEDED
            var now = DateTime.UtcNow.AddHours(8);
            var budget = _context.Budgets.Find(b =>
                b.UserId == userId &&
                b.CategoryId == expense.CategoryId &&
                b.Month == now.Month &&
                b.Year == now.Year
            ).FirstOrDefault();

            var categories = _context.Categories.Find(_ => true).ToList();
            var categoryName = categories.FirstOrDefault(c => c.CategoryId == expense.CategoryId)?.CategoryName ?? "Unknown";

            if (budget != null)
            {
                // ✅ Calculate current spent amount dynamically
                var currentSpent = _context.Expenses
                    .Find(e => e.UserId == userId &&
                              e.CategoryId == expense.CategoryId &&
                              e.ExpenseDate.Month == now.Month &&
                              e.ExpenseDate.Year == now.Year)
                    .ToList()
                    .Sum(e => e.Amount);

                var remainingBudget = budget.BudgetAmount - currentSpent;

                // If expense exceeds remaining budget, SPLIT IT
                if (expense.Amount > remainingBudget && remainingBudget > 0)
                {
                    // PLANNED PORTION (within budget)
                    var plannedExpense = new Expense
                    {
                        ExpenseId = newId.ToString(),
                        Amount = remainingBudget,
                        CategoryId = expense.CategoryId,
                        CustomCategoryName = !string.IsNullOrEmpty(customCategoryName) ? customCategoryName.Trim() : null,
                        Description = expense.Description ?? "",
                        ExpenseDate = expense.ExpenseDate,
                        CreatedAt = expense.CreatedAt,
                        UserId = userId,
                        IsOverBudget = false,
                        TransactionGroupId = transactionGroupId
                    };
                    _context.Expenses.InsertOne(plannedExpense);
                    // ✅ REMOVED: budget.SpentAmount += remainingBudget;

                    // OVER-BUDGET PORTION (unplanned)
                    newId++;
                    var overBudgetAmount = expense.Amount - remainingBudget;
                    var overBudgetExpense = new Expense
                    {
                        ExpenseId = newId.ToString(),
                        Amount = overBudgetAmount,
                        CategoryId = expense.CategoryId,
                        CustomCategoryName = $"{categoryName} (Unplanned)",
                        Description = $"Over budget - {expense.Description ?? ""}",
                        ExpenseDate = expense.ExpenseDate,
                        CreatedAt = expense.CreatedAt,
                        UserId = userId,
                        IsOverBudget = true,
                        TransactionGroupId = transactionGroupId
                    };
                    _context.Expenses.InsertOne(overBudgetExpense);

                    // ✅ REMOVED: _context.Budgets.ReplaceOne(b => b.Id == budget.Id, budget);

                    TempData["BudgetWarning"] = $"⚠️ Expense split: ₱{remainingBudget:N2} within budget, ₱{overBudgetAmount:N2} marked as over-budget.";
                }
                else if (expense.Amount > remainingBudget && remainingBudget <= 0)
                {
                    // ENTIRE EXPENSE IS OVER BUDGET
                    expense.ExpenseId = newId.ToString();
                    expense.CustomCategoryName = $"{categoryName} (Unplanned)";
                    expense.Description = $"Over budget - {expense.Description ?? ""}";
                    expense.IsOverBudget = true;
                    expense.TransactionGroupId = transactionGroupId;
                    _context.Expenses.InsertOne(expense);

                    TempData["BudgetWarning"] = $"⚠️ Budget exceeded! This entire expense is marked as over-budget.";
                }
                else
                {
                    // NORMAL: Within budget
                    expense.ExpenseId = newId.ToString();
                    if (!string.IsNullOrEmpty(customCategoryName))
                        expense.CustomCategoryName = customCategoryName.Trim();
                    expense.IsOverBudget = false;
                    expense.TransactionGroupId = transactionGroupId;
                    _context.Expenses.InsertOne(expense);
                    // ✅ REMOVED: budget.SpentAmount += expense.Amount;
                    // ✅ REMOVED: _context.Budgets.ReplaceOne(b => b.Id == budget.Id, budget);
                }
            }
            else
            {
                // NO BUDGET: Mark as unplanned
                expense.ExpenseId = newId.ToString();
                if (!string.IsNullOrEmpty(customCategoryName))
                    expense.CustomCategoryName = customCategoryName.Trim();
                expense.IsOverBudget = true; // No budget = unplanned
                expense.TransactionGroupId = transactionGroupId;
                _context.Expenses.InsertOne(expense);
            }

            TempData["SuccessMessage"] = "Expense added successfully!";
            return RedirectToAction("Expenses");
        }

        [HttpPost]
        public IActionResult EditExpense(Expense expense, string? customCategoryName)
        {
            var userId = HttpContext.Session.GetString("UserId");
            var existingExpense = _context.Expenses.Find(e => e.Id == expense.Id && e.UserId == userId).FirstOrDefault();
            if (existingExpense == null)
            {
                TempData["ErrorMessage"] = "Expense not found!";
                return RedirectToAction("Expenses");
            }

            if (!string.IsNullOrEmpty(customCategoryName))
            {
                expense.CustomCategoryName = customCategoryName.Trim();
            }
            else
            {
                expense.CustomCategoryName = null;
            }

            expense.UserId = userId;
            expense.ExpenseId = existingExpense.ExpenseId;
            expense.CreatedAt = existingExpense.CreatedAt;

            _context.Expenses.ReplaceOne(e => e.Id == expense.Id, expense);

            // ✅ REMOVED: All budget.SpentAmount updates since it's calculated dynamically

            TempData["SuccessMessage"] = "Expense updated successfully!";
            return RedirectToAction("Expenses");
        }

        public IActionResult DeleteExpense(string id)
        {
            var userId = HttpContext.Session.GetString("UserId");
            var expense = _context.Expenses.Find(e => e.Id == id && e.UserId == userId).FirstOrDefault();

            if (expense != null)
            {
                // ✅ REMOVED: All budget.SpentAmount updates since it's calculated dynamically
                _context.Expenses.DeleteOne(e => e.Id == id);
                TempData["SuccessMessage"] = "Expense deleted successfully!";
            }

            return RedirectToAction("Expenses");
        }

        public IActionResult Budgets()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var budgets = _context.Budgets.Find(b => b.UserId == userId).ToList();
            var categories = _context.Categories.Find(_ => true).ToList();

            // ✅ Get all expenses to calculate spent amounts
            var allExpenses = _context.Expenses.Find(e => e.UserId == userId).ToList();

            foreach (var budget in budgets)
            {
                budget.CategoryName = categories.FirstOrDefault(c => c.CategoryId == budget.CategoryId)?.CategoryName ?? "Unknown";

                // ✅ Calculate SpentAmount from ALL expenses in this category/month/year
                // Count EVERYTHING - both planned (IsOverBudget=false) and unplanned (IsOverBudget=true)
                budget.SpentAmount = allExpenses
                    .Where(e => e.CategoryId == budget.CategoryId &&
                               e.ExpenseDate.Month == budget.Month &&
                               e.ExpenseDate.Year == budget.Year)
                    .Sum(e => e.Amount);
            }

            var groupedBudgets = budgets
                .GroupBy(b => $"{b.Month:D2}/{b.Year}")
                .ToDictionary(g => g.Key, g => g.ToList());

            return View(groupedBudgets);
        }

        [HttpGet]
        public IActionResult AddBudget()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var now = DateTime.UtcNow.AddHours(8);
            var model = new Budget
            {
                Month = now.Month,
                Year = now.Year
            };

            var categories = _context.Categories.Find(_ => true).ToList();
            ViewBag.Categories = categories;

            return View(model);
        }

        [HttpPost]
        public IActionResult AddBudget(Budget budget)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            budget.UserId = userId;
            var now = DateTime.UtcNow.AddHours(8);
            budget.Month = now.Month;
            budget.Year = now.Year;

            var existingBudget = _context.Budgets.Find(b =>
                b.UserId == userId &&
                b.CategoryId == budget.CategoryId &&
                b.Month == budget.Month &&
                b.Year == budget.Year
            ).FirstOrDefault();

            if (existingBudget != null)
            {
                existingBudget.BudgetAmount += budget.BudgetAmount;
                _context.Budgets.ReplaceOne(b => b.Id == existingBudget.Id, existingBudget);

                var categories = _context.Categories.Find(_ => true).ToList();
                var categoryName = categories.FirstOrDefault(c => c.CategoryId == budget.CategoryId)?.CategoryName ?? "Unknown";

                var overBudgetExpenses = _context.Expenses.Find(e =>
                    e.UserId == userId &&
                    e.CategoryId == budget.CategoryId &&
                    e.IsOverBudget == true &&
                    e.ExpenseDate.Month == now.Month &&
                    e.ExpenseDate.Year == now.Year
                ).ToList();

                // ✅ Calculate current spent amount dynamically
                var currentSpent = _context.Expenses
                    .Find(e => e.UserId == userId &&
                              e.CategoryId == budget.CategoryId &&
                              e.ExpenseDate.Month == now.Month &&
                              e.ExpenseDate.Year == now.Year &&
                              !e.IsOverBudget)
                    .ToList()
                    .Sum(e => e.Amount);

                var transactionGroups = overBudgetExpenses
                    .Where(e => !string.IsNullOrEmpty(e.TransactionGroupId))
                    .GroupBy(e => e.TransactionGroupId)
                    .OrderBy(g => g.First().CreatedAt)
                    .ToList();

                decimal totalMerged = 0;
                int mergedTransactions = 0;

                foreach (var group in transactionGroups)
                {
                    var groupExpenses = group.ToList();
                    var totalGroupAmount = groupExpenses.Sum(e => e.Amount);

                    // ✅ Use dynamically calculated remaining
                    var currentRemaining = existingBudget.BudgetAmount - currentSpent;

                    if (currentRemaining >= totalGroupAmount)
                    {
                        var plannedExpenseInGroup = _context.Expenses.Find(e =>
                            e.UserId == userId &&
                            e.CategoryId == budget.CategoryId &&
                            e.TransactionGroupId == group.Key &&
                            e.IsOverBudget == false
                        ).FirstOrDefault();

                        foreach (var overExpense in groupExpenses)
                        {
                            if (plannedExpenseInGroup != null)
                            {
                                plannedExpenseInGroup.Amount += overExpense.Amount;
                                plannedExpenseInGroup.Description = plannedExpenseInGroup.Description?.Replace("Over budget - ", "").Trim();
                                _context.Expenses.ReplaceOne(e => e.Id == plannedExpenseInGroup.Id, plannedExpenseInGroup);
                                _context.Expenses.DeleteOne(e => e.Id == overExpense.Id);
                            }
                            else
                            {
                                overExpense.CustomCategoryName = overExpense.CustomCategoryName?.Replace(" (Unplanned)", "").Replace($"{categoryName} (Unplanned)", "").Trim();
                                overExpense.Description = overExpense.Description?.Replace("Over budget - ", "").Trim();
                                overExpense.IsOverBudget = false;
                                _context.Expenses.ReplaceOne(e => e.Id == overExpense.Id, overExpense);
                            }

                            // ✅ Update current spent for next iteration
                            currentSpent += overExpense.Amount;
                            totalMerged += overExpense.Amount;
                        }

                        mergedTransactions++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (totalMerged > 0)
                {
                    TempData["SuccessMessage"] = $"Budget updated! New total: ₱{existingBudget.BudgetAmount:N2}. Merged ₱{totalMerged:N2} from {mergedTransactions} transaction(s).";
                }
                else
                {
                    TempData["SuccessMessage"] = $"Budget updated! New total: ₱{existingBudget.BudgetAmount:N2}. Not enough budget to merge over-budget expenses yet.";
                }

                return RedirectToAction("Budgets");
            }

            // ✅ CREATE NEW BUDGET - Removed SpentAmount = 0
            budget.CreatedAt = DateTime.UtcNow;

            var allBudgets = _context.Budgets.Find(_ => true).ToList();
            int newId = 1;
            if (allBudgets.Any())
            {
                var validIds = allBudgets
                    .Where(b => !string.IsNullOrEmpty(b.BudgetId) && int.TryParse(b.BudgetId, out _))
                    .Select(b => int.Parse(b.BudgetId!))
                    .ToList();
                if (validIds.Any())
                    newId = validIds.Max() + 1;
            }
            budget.BudgetId = newId.ToString();

            _context.Budgets.InsertOne(budget);

            TempData["SuccessMessage"] = "Budget added successfully!";
            return RedirectToAction("Budgets");
        }

        public IActionResult EditBudget(string id)
        {
            var userId = HttpContext.Session.GetString("UserId");
            var budget = _context.Budgets.Find(b => b.Id == id && b.UserId == userId).FirstOrDefault();
            if (budget == null)
                return NotFound();

            var categories = _context.Categories.Find(_ => true).ToList();
            ViewBag.Categories = categories;

            return View("EditBudget", budget);
        }

        [HttpPost]
        public IActionResult EditBudget(Budget budget)
        {
            var userId = HttpContext.Session.GetString("UserId");
            var existing = _context.Budgets.Find(b => b.Id == budget.Id && b.UserId == userId).FirstOrDefault();
            if (existing == null)
                return NotFound();

            existing.CategoryId = budget.CategoryId;
            existing.BudgetAmount = budget.BudgetAmount;

            _context.Budgets.ReplaceOne(b => b.Id == budget.Id, existing);

            TempData["Success"] = "Budget updated successfully!";
            return RedirectToAction("Budgets");
        }

        [HttpGet]
        public IActionResult DeleteBudget(string id)
        {
            var userId = HttpContext.Session.GetString("UserId");
            _context.Budgets.DeleteOne(b => b.Id == id && b.UserId == userId);
            TempData["Success"] = "Budget deleted successfully!";
            return RedirectToAction("Budgets");
        }

        public IActionResult ExpenseTracking(int? year, int? month)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Singapore Standard Time" : "Asia/Manila"
            );
            var currentDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);
            int selectedYear = year ?? currentDate.Year;
            int selectedMonth = month ?? currentDate.Month;

            if (selectedMonth > 12)
            {
                selectedMonth = 1;
                selectedYear++;
            }
            else if (selectedMonth < 1)
            {
                selectedMonth = 12;
                selectedYear--;
            }

            var startDate = new DateTime(selectedYear, selectedMonth, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var budgets = _context.Budgets.Find(b =>
                b.UserId == userId &&
                b.Month == selectedMonth &&
                b.Year == selectedYear
            ).ToList();

            var budgetedCategoryIds = new HashSet<string>(budgets.Select(b => b.CategoryId));

            var expenses = _context.Expenses
                .Find(e => e.UserId == userId && e.ExpenseDate >= startDate && e.ExpenseDate <= endDate)
                .ToList();

            var categories = _context.Categories.Find(_ => true).ToList();

            expenses = expenses.Where(e => categories.Any(c => c.CategoryId == e.CategoryId)).ToList();

            foreach (var expense in expenses)
            {
                expense.CategoryName = categories.FirstOrDefault(c => c.CategoryId == expense.CategoryId)?.CategoryName ?? "Unknown";
            }

            var groupedData = expenses
                .GroupBy(e => TimeZoneInfo.ConvertTime(e.ExpenseDate, phTimeZone).Day)
                .Select(g => new
                {
                    Day = g.Key,
                    Categories = g.GroupBy(e => e.DisplayCategoryName)
                                  .Select(c => new
                                  {
                                      Category = c.Key,
                                      Total = c.Sum(e => e.Amount),
                                      Description = string.Join(", ", c.Select(x => x.Description)),
                                      CategoryId = c.First().CategoryId,
                                      IsPlanned = budgetedCategoryIds.Contains(c.First().CategoryId)
                                  })
                                  .ToList(),
                    Total = g.Sum(e => e.Amount)
                })
                .OrderBy(x => x.Day)
                .ToList();

            var monthName = new DateTime(selectedYear, selectedMonth, 1).ToString("MMMM yyyy");

            var categoryColors = new Dictionary<string, string>
            {
                { "Food", "#e57373" },
                { "Bills", "#fbc02d" },
                { "Transportation", "#64b5f6" },
                { "Others", "#90a4ae" }
            };

            ViewBag.GroupedData = System.Text.Json.JsonSerializer.Serialize(groupedData);
            ViewBag.CategoryColors = System.Text.Json.JsonSerializer.Serialize(categoryColors);
            ViewBag.MonthName = monthName;
            ViewBag.Month = selectedMonth;
            ViewBag.Year = selectedYear;

            return View();
        }

        [HttpGet]
        public IActionResult Export(DateTime? startDate, DateTime? endDate, string format)
        {
            if (format == "excel")
                return ExportToExcel(startDate, endDate);

            return ExportToPDF(startDate, endDate);
        }

        [HttpGet]
        public IActionResult ExportToExcel(DateTime? startDate, DateTime? endDate)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var expenses = _context.Expenses.Find(e => e.UserId == userId).ToList();

            if (startDate.HasValue && endDate.HasValue)
            {
                var endDateInclusive = endDate.Value.Date.AddDays(1).AddSeconds(-1);
                expenses = expenses.Where(e => e.ExpenseDate >= startDate && e.ExpenseDate <= endDateInclusive).ToList();
            }

            var categories = _context.Categories.Find(_ => true).ToList();
            foreach (var expense in expenses)
            {
                expense.CategoryName = categories.FirstOrDefault(c => c.CategoryId == expense.CategoryId)?.CategoryName ?? "Unknown";
            }

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Expenses");

            worksheet.Cell(1, 1).Value = "Date";
            worksheet.Cell(1, 2).Value = "Category";
            worksheet.Cell(1, 3).Value = "Amount";
            worksheet.Cell(1, 4).Value = "Description";
            worksheet.Cell(1, 5).Value = "Type";

            var headerRange = worksheet.Range(1, 1, 1, 5);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            int row = 2;
            foreach (var e in expenses)
            {
                worksheet.Cell(row, 1).Value = e.ExpenseDate.ToString("MMM dd, yyyy");
                worksheet.Cell(row, 2).Value = e.DisplayCategoryName;
                worksheet.Cell(row, 3).Value = e.Amount;
                worksheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(row, 4).Value = e.Description;
                worksheet.Cell(row, 5).Value = e.IsOverBudget ? "Unplanned" : "";
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var username = HttpContext.Session.GetString("Username") ?? "User";

            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"ExpenseReport_{username}.xlsx");
        }

        private IActionResult ExportToPDF(DateTime? startDate, DateTime? endDate)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Singapore Standard Time" : "Asia/Manila"
            );
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone);

            // Determine which month to show summary for
            int summaryMonth, summaryYear;
            if (startDate.HasValue)
            {
                summaryMonth = startDate.Value.Month;
                summaryYear = startDate.Value.Year;
            }
            else
            {
                summaryMonth = now.Month;
                summaryYear = now.Year;
            }

            // Set default date range if not provided
            if (!startDate.HasValue || !endDate.HasValue)
            {
                startDate = new DateTime(now.Year, now.Month, 1);
                endDate = now.Date;
            }

            // Extend endDate to include entire day
            var endDateInclusive = endDate.Value.Date.AddDays(1).AddSeconds(-1);

            // Get expenses for the EXPORT date range (what shows in table)
            var expensesForTable = _context.Expenses
                .Find(e => e.UserId == userId && e.ExpenseDate >= startDate && e.ExpenseDate <= endDateInclusive)
                .ToList();

            // Get ALL expenses for the SUMMARY MONTH (for the top cards)
            var allMonthExpenses = _context.Expenses
                .Find(e => e.UserId == userId &&
                          e.ExpenseDate.Month == summaryMonth &&
                          e.ExpenseDate.Year == summaryYear)
                .ToList();

            var categories = _context.Categories.Find(_ => true).ToList();

            // Process both sets of expenses
            foreach (var expense in expensesForTable.Concat(allMonthExpenses).Distinct())
            {
                expense.CategoryName = categories.FirstOrDefault(c => c.CategoryId == expense.CategoryId)?.CategoryName ?? "Unknown";
            }

            // Get budgets for the SUMMARY MONTH
            var budgets = _context.Budgets.Find(b =>
                b.UserId == userId &&
                b.Month == summaryMonth &&
                b.Year == summaryYear
            ).ToList();

            foreach (var budget in budgets)
            {
                budget.CategoryName = categories.FirstOrDefault(c => c.CategoryId == budget.CategoryId)?.CategoryName ?? "Unknown";
            }

            // Calculate summary based on ENTIRE MONTH
            decimal totalBudget = budgets.Sum(b => b.BudgetAmount);
            decimal totalExpenses = allMonthExpenses.Sum(e => e.Amount);
            decimal remaining = totalBudget - totalExpenses;

            // Calculate ACTUAL over budget amount: expenses that exceed their category budget
            decimal overBudget = 0;
            foreach (var budget in budgets)
            {
                var categoryExpenses = allMonthExpenses
                    .Where(e => e.CategoryId == budget.CategoryId)
                    .Sum(e => e.Amount);

                if (categoryExpenses > budget.BudgetAmount)
                {
                    overBudget += (categoryExpenses - budget.BudgetAmount);
                }
            }

            // Also add expenses from categories with NO budget
            var categoriesWithBudget = budgets.Select(b => b.CategoryId).ToHashSet();
            var expensesWithoutBudget = allMonthExpenses
                .Where(e => !categoriesWithBudget.Contains(e.CategoryId))
                .Sum(e => e.Amount);
            overBudget += expensesWithoutBudget;

            var categoryTotals = allMonthExpenses
                .GroupBy(e => e.CategoryName)
                .Select(g => new
                {
                    Category = g.Key,
                    Total = g.Sum(e => e.Amount)
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            using var stream = new MemoryStream();
            var document = new Document(PageSize.A4, 40, 40, 40, 40);
            PdfWriter.GetInstance(document, stream);
            document.Open();

            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 24, new BaseColor(88, 54, 71));
            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, BaseColor.DARK_GRAY);
            var subHeaderFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, BaseColor.BLACK);
            var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10, BaseColor.BLACK);
            var smallFont = FontFactory.GetFont(FontFactory.HELVETICA, 8, BaseColor.GRAY);

            // Header
            var headerTable = new PdfPTable(1) { WidthPercentage = 100 };
            var titleCell = new PdfPCell(new Phrase("FINANCE\nTRACKER", titleFont))
            {
                Border = Rectangle.NO_BORDER,
                HorizontalAlignment = Element.ALIGN_CENTER,
                VerticalAlignment = Element.ALIGN_MIDDLE,
                PaddingBottom = 20
            };
            headerTable.AddCell(titleCell);
            document.Add(headerTable);

            // Summary Cards
            var summaryTable = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 20 };
            summaryTable.SetWidths(new float[] { 1, 1, 1, 1 });

            // Budget Box
            var budgetBox = new PdfPTable(1) { WidthPercentage = 100 };
            var budgetHeaderCell = new PdfPCell(new Phrase("BUDGET", subHeaderFont))
            {
                BackgroundColor = new BaseColor(173, 216, 230),
                Padding = 8,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = Rectangle.NO_BORDER
            };
            budgetBox.AddCell(budgetHeaderCell);

            var budgetAmountCell = new PdfPCell(new Phrase($"₱{totalBudget:N2}",
                FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, BaseColor.BLACK)))
            {
                BackgroundColor = new BaseColor(255, 255, 255),
                Padding = 15,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = Rectangle.BOX
            };
            budgetBox.AddCell(budgetAmountCell);
            summaryTable.AddCell(new PdfPCell(budgetBox) { Border = Rectangle.NO_BORDER, Padding = 5 });

            // Expense Box
            var expenseBox = new PdfPTable(1) { WidthPercentage = 100 };
            var expenseHeaderCell = new PdfPCell(new Phrase("EXPENSE", subHeaderFont))
            {
                BackgroundColor = new BaseColor(255, 182, 193),
                Padding = 8,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = Rectangle.NO_BORDER
            };
            expenseBox.AddCell(expenseHeaderCell);

            var expenseAmountCell = new PdfPCell(new Phrase($"₱{totalExpenses:N2}",
                FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, BaseColor.BLACK)))
            {
                BackgroundColor = new BaseColor(255, 255, 255),
                Padding = 15,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = Rectangle.BOX
            };
            expenseBox.AddCell(expenseAmountCell);
            summaryTable.AddCell(new PdfPCell(expenseBox) { Border = Rectangle.NO_BORDER, Padding = 5 });

            // Total Amount Left Box
            var leftBox = new PdfPTable(1) { WidthPercentage = 100 };
            var leftHeaderCell = new PdfPCell(new Phrase("AMOUNT LEFT", subHeaderFont))
            {
                BackgroundColor = new BaseColor(144, 238, 144),
                Padding = 8,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = Rectangle.NO_BORDER
            };
            leftBox.AddCell(leftHeaderCell);

            var leftAmountCell = new PdfPCell(new Phrase($"₱{remaining:N2}",
                FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, remaining >= 0 ? new BaseColor(0, 128, 0) : new BaseColor(255, 0, 0))))
            {
                BackgroundColor = new BaseColor(255, 255, 255),
                Padding = 15,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = Rectangle.BOX
            };
            leftBox.AddCell(leftAmountCell);
            summaryTable.AddCell(new PdfPCell(leftBox) { Border = Rectangle.NO_BORDER, Padding = 5 });

            // Over Budget Box
            var overBox = new PdfPTable(1) { WidthPercentage = 100 };
            var overHeaderCell = new PdfPCell(new Phrase("OVER BUDGET", subHeaderFont))
            {
                BackgroundColor = new BaseColor(255, 160, 122),
                Padding = 8,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = Rectangle.NO_BORDER
            };
            overBox.AddCell(overHeaderCell);

            var overAmountCell = new PdfPCell(new Phrase($"₱{overBudget:N2}",
                FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, overBudget > 0 ? new BaseColor(255, 0, 0) : BaseColor.BLACK)))
            {
                BackgroundColor = new BaseColor(255, 255, 255),
                Padding = 15,
                HorizontalAlignment = Element.ALIGN_CENTER,
                Border = Rectangle.BOX
            };
            overBox.AddCell(overAmountCell);
            summaryTable.AddCell(new PdfPCell(overBox) { Border = Rectangle.NO_BORDER, Padding = 5 });

            document.Add(summaryTable);

            // Transaction Table
            document.Add(new Paragraph("TRANSACTION HISTORY", headerFont) { SpacingBefore = 10, SpacingAfter = 10 });

            var table = new PdfPTable(6) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 0.8f, 1.5f, 2.5f, 1.8f, 1.2f, 1f });

            var headers = new[] { "No.", "Date", "Description", "Category", "Amount ₱", "Type" };
            foreach (var header in headers)
            {
                table.AddCell(new PdfPCell(new Phrase(header, subHeaderFont))
                {
                    BackgroundColor = new BaseColor(240, 240, 240),
                    Padding = 8,
                    HorizontalAlignment = Element.ALIGN_CENTER
                });
            }

            // Create lookup for which categories have budgets
            var categoriesWithBudgetLookup = budgets.Select(b => b.CategoryId).ToHashSet();

            int counter = 1;
            foreach (var expense in expensesForTable.OrderBy(e => e.ExpenseDate))
            {
                var phDate = TimeZoneInfo.ConvertTime(expense.ExpenseDate, phTimeZone);

                table.AddCell(new PdfPCell(new Phrase(counter.ToString(), normalFont)) { Padding = 5, HorizontalAlignment = Element.ALIGN_CENTER });
                table.AddCell(new PdfPCell(new Phrase(phDate.ToString("MMM d, yyyy"), normalFont)) { Padding = 5 });
                table.AddCell(new PdfPCell(new Phrase(expense.Description ?? "-", normalFont)) { Padding = 5 });

                var categoryCell = new PdfPCell(new Phrase(expense.DisplayCategoryName, normalFont))
                {
                    Padding = 5,
                    BackgroundColor = GetCategoryColor(expense.CategoryName)
                };
                table.AddCell(categoryCell);

                table.AddCell(new PdfPCell(new Phrase($"₱{expense.Amount:N2}", normalFont))
                { Padding = 5, HorizontalAlignment = Element.ALIGN_RIGHT });

                // Check if this expense's category has a budget
                bool hasNoBudget = !categoriesWithBudgetLookup.Contains(expense.CategoryId);
                var typeText = hasNoBudget ? "Unplanned" : "";

                var typeCell = new PdfPCell(new Phrase(typeText, normalFont))
                {
                    Padding = 5,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    BackgroundColor = hasNoBudget ? new BaseColor(255, 235, 205) : BaseColor.WHITE
                };
                table.AddCell(typeCell);

                counter++;
            }

            document.Add(table);

            // Footer
            document.Add(new Paragraph(" "));
            document.Add(new Paragraph($"Generated on: {now:MMMM dd, yyyy hh:mm tt}", smallFont) { Alignment = Element.ALIGN_RIGHT });
            document.Add(new Paragraph($"Period: {startDate:MMM dd, yyyy} - {endDate:MMM dd, yyyy}", smallFont) { Alignment = Element.ALIGN_RIGHT });

            document.Close();

            var username = HttpContext.Session.GetString("Username") ?? "User";
            return File(stream.ToArray(), "application/pdf", $"FinanceReport_{username}_{now:yyyyMMdd}.pdf");
        }

        private BaseColor GetCategoryColor(string categoryName)
        {
            return categoryName switch
            {
                "Food" => new BaseColor(255, 192, 203),
                "Bills" => new BaseColor(255, 235, 205),
                "Transportation" => new BaseColor(173, 216, 230),
                "Others" => new BaseColor(211, 211, 211),
                _ => new BaseColor(245, 245, 245)
            };
        }
    }
}