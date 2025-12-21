# 💰 FinanceTracker

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![MongoDB](https://img.shields.io/badge/MongoDB-Enabled-47A248?style=for-the-badge&logo=mongodb&logoColor=white)
![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS-3.4-06B6D4?style=for-the-badge&logo=tailwindcss&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?style=for-the-badge&logo=docker&logoColor=white)
![MVC](https://img.shields.io/badge/Arch-MVC-blue?style=for-the-badge)

> **A robust, high-performance personal finance manager built with ASP.NET Core 8.**

FinanceTracker combines the raw performance of **.NET 8** with the flexibility of **MongoDB** to deliver a seamless, server-rendered financial management experience. Styled with **Tailwind CSS** for a premium, dark-mode-first aesthetic, it captures the speed of a modern web app with the reliability of enterprise-grade backend architecture.

## ✨ Features

### 💵 Core Financial Tracking
- **Unified Dashboard**: Get a birds-eye view of your financial health, net balance, and recent transaction history.
- **Expense Logging**: Rapidly add expenses with categorized tagging and detailed notes.
- **Income Management**: Track multiple income streams seamlessly.

### 📊 Advanced Budgeting
- **Smart Budgets**: Set monthly limits for specific categories (e.g., Groceries, Rent) and track usage percentages in real-time.
- **Visual Analytics**: Interactive charts and breakdowns (Pie/Bar) to visualize spending habits.
- **Monthly Reports**: Generate detailed reports to analyze month-over-month performance.

### 🛠️ Professional Tools
- **Export Capabilities**: Built-in support to export financial data to **Excel** (ClosedXML) and **PDF** (QuestPDF) for offline archiving.
- **Secure Authentication**: Robust user session management with `Microsoft.AspNetCore.Session` and Cookie-based auth.
- **Responsive Design**: fully responsive layout that works perfectly on desktop and mobile devices.

---

## 🚀 Getting Started

Follow these instructions to get the project up and running on your local machine.

### Prerequisites
- **.NET 8 SDK**
- **MongoDB** (Local instance or Atlas connection string)
- **Docker** (Optional, for containerized run)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/FinanceTracker.git
   cd FinanceTracker
   ```

2. **Configuration**
   Update `appsettings.json` with your MongoDB connection string:
   ```json
   {
     "ConnectionStrings": {
       "MongoDbConnection": "mongodb://localhost:27017"
     },
     "DatabaseName": "FinanceTrackerDB"
   }
   ```

3. **Run the Application**
   ```bash
   dotnet watch run
   ```
   The application will launch typically at `http://127.0.0.1:10000/` or the port configured in your environment.

### 🐳 Running with Docker

Build and run the containerized application:

```bash
docker build -t financetracker .
docker run -d -p 8080:80 --name tracker financetracker
```

---

## 🛠️ Tech Stack

Built with an industry-standard stack focused on performance and maintainability.

| Component | Technology | Description |
|---|---|---|
| **Core Framework** | **.NET 8** | High-performance, cross-platform framework. |
| **Architecture** | **MVC** | Model-View-Controller design pattern for clean separation of concerns. |
| **Database** | **MongoDB** | NoSQL document database for flexible schema design. |
| **Styling** | **Tailwind CSS** | Utility-first CSS framework for rapid UI development (CDN-based). |
| **Reporting** | **QuestPDF & ClosedXML** | Libraries for generating professional PDF and Excel reports. |
