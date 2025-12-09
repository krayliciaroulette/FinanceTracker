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
            ViewBag.Categories = categories;

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

            // Calculate Summary Stats
            decimal totalSpent = expenses.Sum(e => e.Amount);
            int daysInMonth = DateTime.DaysInMonth(selectedYear, selectedMonth);
            int daysPassed = (selectedYear == currentDate.Year && selectedMonth == currentDate.Month) 
                             ? currentDate.Day 
                             : daysInMonth;
            
            decimal dailyAverage = daysPassed > 0 ? totalSpent / daysPassed : 0;

            var highestDayGroup = groupedData.OrderByDescending(d => d.Total).FirstOrDefault();
            decimal highestDayAmount = highestDayGroup != null ? highestDayGroup.Total : 0;
            int highestDay = highestDayGroup?.Day ?? 0;

            ViewBag.TotalSpent = totalSpent;
            ViewBag.DailyAverage = dailyAverage;
            ViewBag.HighestDayAmount = highestDayAmount;
            ViewBag.HighestDay = highestDay;

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

            // Set default date range if not provided
            if (!startDate.HasValue || !endDate.HasValue)
            {
                startDate = new DateTime(now.Year, now.Month, 1);
                endDate = now.Date;
            }

            // Extend endDate to include entire day
            var endDateInclusive = endDate.Value.Date.AddDays(1).AddSeconds(-1);

            // ✅ LOGIC FIX: Get Expenses for the ENTIRE Date Range
            var expensesForRange = _context.Expenses
                .Find(e => e.UserId == userId && e.ExpenseDate >= startDate && e.ExpenseDate <= endDateInclusive)
                .ToList();

            // ✅ LOGIC FIX: Get Budgets for ALL months within the Date Range
            // Construct a list of Month/Year pairs that are covered by the range
            var relevantBudgets = new List<Budget>();
            var cursorDate = startDate.Value;
            while (cursorDate <= endDateInclusive)
            {
                var monthBudgets = _context.Budgets.Find(b =>
                    b.UserId == userId &&
                    b.Month == cursorDate.Month &&
                    b.Year == cursorDate.Year
                ).ToList();
                relevantBudgets.AddRange(monthBudgets);
                cursorDate = cursorDate.AddMonths(1);
            }

            var categories = _context.Categories.Find(_ => true).ToList();

            // Enhance expenses with category names
            foreach (var expense in expensesForRange)
            {
                expense.CategoryName = categories.FirstOrDefault(c => c.CategoryId == expense.CategoryId)?.CategoryName ?? "Unknown";
            }

            // Enhance budgets with category names
            foreach (var budget in relevantBudgets)
            {
                budget.CategoryName = categories.FirstOrDefault(c => c.CategoryId == budget.CategoryId)?.CategoryName ?? "Unknown";
            }

            // ✅ CALCULATE SUMMARIES FOR THE ENTIRE PERIOD
            decimal totalBudget = relevantBudgets.Sum(b => b.BudgetAmount);
            decimal totalExpenses = expensesForRange.Sum(e => e.Amount);
            decimal remaining = totalBudget - totalExpenses; // Overall Remaining

            // Calculate ACTUAL Over Budget (Sum of overages per category per month OR aggregate? Let's do aggregate per category for the period)
            // Actually, for multi-month, simply summing (Total Expense > Total Budget) is safer and clearer for "Period Summary".
            // But to be precise, we should sum the overages.
            // Let's stick to the period aggregate: If (Total Expense for Cat X) > (Total Budget for Cat X), add diff.

            var categoryBudgetSums = relevantBudgets
                .GroupBy(b => b.CategoryId)
                .ToDictionary(g => g.Key, g => g.Sum(b => b.BudgetAmount));

            var categoryExpenseSums = expensesForRange
                .GroupBy(e => e.CategoryId)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

            decimal overBudget = 0;
            // Add overages for budgeted categories
            foreach (var catId in categoryBudgetSums.Keys)
            {
                decimal bTotal = categoryBudgetSums[catId];
                decimal eTotal = categoryExpenseSums.ContainsKey(catId) ? categoryExpenseSums[catId] : 0;
                if (eTotal > bTotal)
                {
                    overBudget += (eTotal - bTotal);
                }
            }
            // Add expenses for categories with NO budget at all in this period
            foreach (var catId in categoryExpenseSums.Keys)
            {
                if (!categoryBudgetSums.ContainsKey(catId))
                {
                    overBudget += categoryExpenseSums[catId];
                }
            }


            using var stream = new MemoryStream();
            var document = new Document(PageSize.A4, 30, 30, 40, 40); // Tighter margins
            PdfWriter.GetInstance(document, stream);
            document.Open();

            // COLORS
            var primaryColor = new BaseColor(30, 41, 59); // Slate 800
            var accentColor = new BaseColor(59, 130, 246); // Blue 500
            var lightGray = new BaseColor(248, 250, 252); // Slate 50
            var darkGray = new BaseColor(51, 65, 85); // Slate 700

            // FONTS
            var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 22, primaryColor);
            var subtitleFont = FontFactory.GetFont(FontFactory.HELVETICA, 10, BaseColor.GRAY);
            var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, BaseColor.WHITE);
            var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 9, darkGray);
            var boldFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, darkGray);
            var cardLabelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, new BaseColor(100, 116, 139)); // Slate 500
            var cardValueFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16, primaryColor);

            // --- HEADER SECTION ---
            var headerTable = new PdfPTable(2) { WidthPercentage = 100 };
            headerTable.SetWidths(new float[] { 2, 1 });

            // Title Pair
            var titleCell = new PdfPCell { Border = Rectangle.NO_BORDER, VerticalAlignment = Element.ALIGN_MIDDLE };
            titleCell.AddElement(new Paragraph("FINANCE TRACKER", titleFont));
            titleCell.AddElement(new Paragraph($"Export Period: {startDate:MMM dd, yyyy} - {endDate:MMM dd, yyyy}", subtitleFont));
            headerTable.AddCell(titleCell);

            // Logo/Branding (Text for now)
            var brandingCell = new PdfPCell(new Paragraph("REPORT", 
                FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 24, new BaseColor(241, 245, 249)))) 
                { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT, VerticalAlignment = Element.ALIGN_MIDDLE };
            headerTable.AddCell(brandingCell);
            
            document.Add(headerTable);
            document.Add(new Paragraph(" ") { SpacingAfter = 10 });

            // --- SUMMARY CARDS SECTION ---
            var summaryTable = new PdfPTable(4) { WidthPercentage = 100, SpacingAfter = 25 };
            summaryTable.SetWidths(new float[] { 1, 1, 1, 1 });
            summaryTable.DefaultCell.Border = Rectangle.NO_BORDER;
            summaryTable.DefaultCell.Padding = 5;

            // Helper to create cards
            PdfPCell CreateCard(string label, string value, BaseColor stripeColor)
            {
                var card = new PdfPTable(1) { WidthPercentage = 100 };
                var cell = new PdfPCell { Border = Rectangle.NO_BORDER, BackgroundColor = new BaseColor(255, 255, 255), Padding = 15 };
                
                // Top Border Stripe
                var stripe = new PdfPCell { FixedHeight = 3, BackgroundColor = stripeColor, Border = Rectangle.NO_BORDER };
                card.AddCell(stripe);

                cell.AddElement(new Paragraph(label, cardLabelFont));
                cell.AddElement(new Paragraph(value, cardValueFont));
                card.AddCell(cell);
                
                // Wrapper cell
                var wrapper = new PdfPCell(card) { Border = Rectangle.NO_BORDER, Padding = 4 };
                return wrapper;
            }

            summaryTable.AddCell(CreateCard("TOTAL BUDGET", $"₱{totalBudget:N2}", accentColor));
            summaryTable.AddCell(CreateCard("TOTAL EXPENSE", $"₱{totalExpenses:N2}", new BaseColor(239, 68, 68))); // Red 500
            summaryTable.AddCell(CreateCard("REMAINING", $"₱{remaining:N2}", remaining >= 0 ? new BaseColor(34, 197, 94) : new BaseColor(239, 68, 68))); // Green or Red
            summaryTable.AddCell(CreateCard("OVER BUDGET", $"₱{overBudget:N2}", new BaseColor(245, 158, 11))); // Amber 500

            document.Add(summaryTable);


            // --- TRANSACTIONS TABLE SECTION ---
            document.Add(new Paragraph("TRANSACTION HISTORY", FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, primaryColor)) { SpacingAfter = 10 });

            var table = new PdfPTable(5) { WidthPercentage = 100 };
            table.SetWidths(new float[] { 1.2f, 2.5f, 1.5f, 1.2f, 1f }); // Adjusted widths
            
            // Table Header Config
            var headerStyle = new PdfPCell
            {
                BackgroundColor = primaryColor,
                PaddingTop = 8,
                PaddingBottom = 8,
                PaddingLeft = 10,
                PaddingRight = 10,
                Border = Rectangle.NO_BORDER
            };

            void AddHeader(string text, int align = Element.ALIGN_LEFT)
            {
                var cell = new PdfPCell(headerStyle) { HorizontalAlignment = align };
                cell.Phrase = new Phrase(text, headerFont);
                table.AddCell(cell);
            }

            AddHeader("DATE");
            AddHeader("DESCRIPTION");
            AddHeader("CATEGORY");
            AddHeader("AMOUNT", Element.ALIGN_RIGHT);
            AddHeader("TYPE", Element.ALIGN_CENTER);

            // Table Rows
            int rowIndex = 0;
            foreach (var expense in expensesForRange.OrderBy(e => e.ExpenseDate))
            {
                var phDate = TimeZoneInfo.ConvertTime(expense.ExpenseDate, phTimeZone);
                var isOdd = rowIndex % 2 != 0;
                var rowColor = isOdd ? lightGray : BaseColor.WHITE;

                var rowStyle = new PdfPCell
                {
                    BackgroundColor = rowColor,
                    PaddingTop = 7,
                    PaddingBottom = 7,
                    PaddingLeft = 10,
                    PaddingRight = 10,
                    Border = Rectangle.NO_BORDER,
                    VerticalAlignment = Element.ALIGN_MIDDLE
                };

                void AddCell(string text, Font font, int align = Element.ALIGN_LEFT)
                {
                    var cell = new PdfPCell(rowStyle) { HorizontalAlignment = align };
                    cell.Phrase = new Phrase(text, font);
                    table.AddCell(cell);
                }

                AddCell(phDate.ToString("MMM dd, yyyy"), normalFont);
                AddCell(expense.Description ?? "-", normalFont);
                AddCell(expense.DisplayCategoryName, boldFont);
                AddCell($"₱{expense.Amount:N2}", boldFont, Element.ALIGN_RIGHT);

                // Type (Unplanned/Planned) logic
                bool hasNoBudget = !categoryBudgetSums.ContainsKey(expense.CategoryId);
                string typeText = hasNoBudget ? "UNPLANNED" : "PLANNED";
                
                // Custom pill for Type
                var typeCell = new PdfPCell(rowStyle) { HorizontalAlignment = Element.ALIGN_CENTER };
                var chunk = new Chunk(typeText, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, hasNoBudget ? BaseColor.WHITE : darkGray));
                chunk.SetBackground(hasNoBudget ? new BaseColor(239, 68, 68) : new BaseColor(226, 232, 240)); // Red or Slate 200
                // Note: Chunk background is limited. For proper badges we need simpler text.
                // Let's stick to text color for simplicity and cleanliness.
                var statusFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 7, hasNoBudget ? new BaseColor(220, 38, 38) : new BaseColor(71, 85, 105));
                typeCell.Phrase = new Phrase(typeText, statusFont);
                table.AddCell(typeCell);

                rowIndex++;
            }

            document.Add(table);

            // Footer
            // Note: Canvas manipulation is complex in this context. 
            // Simplified Footer
            document.Add(new Paragraph(" ") { SpacingBefore = 20 });
            var footerLine = new Paragraph($"Generated on {now:MMMM dd, yyyy} at {now:hh:mm tt}", subtitleFont) 
            { Alignment = Element.ALIGN_CENTER };
            document.Add(footerLine);

            document.Close();

            var username = HttpContext.Session.GetString("Username") ?? "User";
            return File(stream.ToArray(), "application/pdf", $"FinanceReport_{username}_{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.pdf");
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