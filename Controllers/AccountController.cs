using ExpenseTracker.Database;
using ExpenseTracker.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;

namespace ExpenseTracker.Controllers
{
    public class AccountController : Controller
    {
        private readonly FinanceContext _context;

        public AccountController(FinanceContext context)
        {
            _context = context;
        }

        public IActionResult Login()
        {
            if (HttpContext.Session.GetString("Username") != null)
                return RedirectToAction("Index", "Finance");
            return View();
        }

        [HttpPost]
        public IActionResult Login(string username, string password)
        {
            // ✅ Find user first
            var user = _context.Users.Find(u => u.Username == username).FirstOrDefault();

            if (user == null)
            {
                ViewBag.Error = "Invalid username or password!";
                return View();
            }

            // ✅ Hash the entered password
            var hashedPassword = HashPassword(password);

            // ✅ Check both plain text (old users) and hashed (new users)
            bool isPasswordCorrect = false;

            // Check if password is already hashed (new user)
            if (user.Password == hashedPassword)
            {
                isPasswordCorrect = true;
            }
            // Check if password is plain text (old user)
            else if (user.Password == password)
            {
                isPasswordCorrect = true;

                // ✅ Auto-upgrade old password to hashed
                user.Password = hashedPassword;
                _context.Users.ReplaceOne(u => u.Id == user.Id, user);
            }

            if (!isPasswordCorrect)
            {
                ViewBag.Error = "Invalid username or password!";
                return View();
            }

            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("UserId", user.UserId ?? "");
            return RedirectToAction("Index", "Finance");
        }

        public IActionResult Register() => View();

        [HttpPost]
        public IActionResult Register(User newUser)
        {
            // ✅ Username uniqueness check
            var existingUser = _context.Users.Find(u => u.Username == newUser.Username).FirstOrDefault();
            if (existingUser != null)
            {
                ViewBag.Error = "Username already exists! Please choose another.";
                return View(newUser);
            }

            // ✅ Email uniqueness check (check against all emails in array)
            var existingEmail = _context.Users.Find(u => u.Email.Contains(newUser.PrimaryEmail)).FirstOrDefault();
            if (existingEmail != null)
            {
                ViewBag.Error = "Email already registered!";
                return View(newUser);
            }

            // ✅ NEW: Phone number uniqueness check
            var existingPhone = _context.Users.Find(u => u.PhoneNumber.Contains(newUser.PrimaryPhoneNumber)).FirstOrDefault();
            if (existingPhone != null)
            {
                ViewBag.Error = "Phone number already registered!";
                return View(newUser);
            }

            // ✅ Password match validation
            if (newUser.Password != newUser.ConfirmPassword)
            {
                ViewBag.Error = "Passwords do not match!";
                return View(newUser);
            }

            // ✅ HASH PASSWORD BEFORE SAVING
            newUser.Password = HashPassword(newUser.Password);

            // ✅ Generate user_id without reading all users (avoids deserialization error)
            var lastUser = _context.Users
                .Find(_ => true)
                .SortByDescending(u => u.UserId)
                .Limit(1)
                .FirstOrDefault();

            int newId = 1;
            if (lastUser != null && !string.IsNullOrEmpty(lastUser.UserId) && int.TryParse(lastUser.UserId, out int lastId))
            {
                newId = lastId + 1;
            }
            newUser.UserId = newId.ToString();

            // ✅ Insert user with all required fields
            _context.Users.InsertOne(newUser);

            TempData["RegisterSuccess"] = true;
            return RedirectToAction("RegisterSuccess");
        }

        // ✅ ADD PASSWORD HASHING METHOD
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        public IActionResult RegisterSuccess() => View();

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}