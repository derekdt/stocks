#region imports
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Globalization;
    using System.Drawing;
    using QuantConnect;
    using QuantConnect.Algorithm.Framework;
    using QuantConnect.Algorithm.Framework.Selection;
    using QuantConnect.Algorithm.Framework.Alphas;
    using QuantConnect.Algorithm.Framework.Portfolio;
    using QuantConnect.Algorithm.Framework.Portfolio.SignalExports;
    using QuantConnect.Algorithm.Framework.Execution;
    using QuantConnect.Algorithm.Framework.Risk;
    using QuantConnect.Algorithm.Selection;
    using QuantConnect.Api;
    using QuantConnect.Parameters;
    using QuantConnect.Benchmarks;
    using QuantConnect.Brokerages;
    using QuantConnect.Commands;
    using QuantConnect.Configuration;
    using QuantConnect.Util;
    using QuantConnect.Interfaces;
    using QuantConnect.Algorithm;
    using QuantConnect.Indicators;
    using QuantConnect.Data;
    using QuantConnect.Data.Auxiliary;
    using QuantConnect.Data.Consolidators;
    using QuantConnect.Data.Custom;
    using QuantConnect.Data.Custom.IconicTypes;
    using QuantConnect.DataSource;
    using QuantConnect.Data.Fundamental;
    using QuantConnect.Data.Market;
    using QuantConnect.Data.Shortable;
    using QuantConnect.Data.UniverseSelection;
    using QuantConnect.Notifications;
    using QuantConnect.Orders;
    using QuantConnect.Orders.Fees;
    using QuantConnect.Orders.Fills;
    using QuantConnect.Orders.OptionExercise;
    using QuantConnect.Orders.Slippage;
    using QuantConnect.Orders.TimeInForces;
    using QuantConnect.Python;
    using QuantConnect.Scheduling;
    using QuantConnect.Securities;
    using QuantConnect.Securities.Equity;
    using QuantConnect.Securities.Future;
    using QuantConnect.Securities.Option;
    using QuantConnect.Securities.Positions;
    using QuantConnect.Securities.Forex;
    using QuantConnect.Securities.Crypto;
    using QuantConnect.Securities.CryptoFuture;
    using QuantConnect.Securities.IndexOption;
    using QuantConnect.Securities.Interfaces;
    using QuantConnect.Securities.Volatility;
    using QuantConnect.Statistics;
    using QCAlgorithmFramework = QuantConnect.Algorithm.QCAlgorithm;
    using QCAlgorithmFrameworkBridge = QuantConnect.Algorithm.QCAlgorithm;
    using Calendar = QuantConnect.Data.Consolidators.Calendar;
#endregion
namespace QuantConnect.Algorithm.CSharp
{

    public class Weekly0dteanalysis : QCAlgorithm
    {
        private Symbol _qqq;

        public override void Initialize()
        {
            // Locally Lean installs free sample data, to download more data please visit https://www.quantconnect.com/docs/v2/lean-cli/datasets/downloading-data
            // Set algorithm start date AFTER the history end date so we can access all historical data
            SetStartDate(2025, 12, 2); // Set Start Date (after history end date)
            SetEndDate(2025, 12, 7); // Set End Date (short period since we're just retrieving history)
            SetCash(100000);             //Set Strategy Cash

            // Add QQQ with daily resolution (we'll consolidate to weekly)
            // Set data normalization to split-adjusted to account for stock splits
            var equity = AddEquity("QQQ", Resolution.Daily);
            equity.SetDataNormalizationMode(DataNormalizationMode.SplitAdjusted);
            _qqq = equity.Symbol;

            // Retrieve historical daily bars for QQQ
            // History() can access data up to the algorithm's start date
            var startDate = new DateTime(2018, 1, 1);
            var endDate = new DateTime(2025, 12, 1);
            var dailyHistory = History(_qqq, startDate, endDate, Resolution.Daily);

            // Consolidate daily bars to weekly bars
            // Note: History() only returns trading days (excludes weekends and holidays)
            // Group trading days by the calendar week they belong to (Monday-Sunday)
            var weeklyBars = new List<TradeBar>();
            var weeklyTradingDates = new Dictionary<DateTime, List<DateTime>>(); // Maps weekly bar time to list of trading dates
            var excludedWeeks = new List<(DateTime WeekStart, int TradingDays, List<DateTime> TradingDates)>();
            
            var groupedByWeek = dailyHistory
                .GroupBy(bar => 
                {
                    // Get the Monday of the calendar week for this trading day
                    // This groups all trading days that fall within the same calendar week
                    var dayOfWeek = (int)bar.Time.DayOfWeek;
                    var mondayOffset = dayOfWeek == 0 ? -6 : 1 - dayOfWeek; // Sunday = 0, so offset by -6
                    return bar.Time.Date.AddDays(mondayOffset);
                })
                .OrderBy(g => g.Key);
            
            foreach (var weekGroup in groupedByWeek)
            {
                var weekBars = weekGroup.OrderBy(b => b.Time).ToList();
                var weekStart = weekGroup.Key;
                
                // Filter out weeks with less than 4 trading days
                // This excludes partial weeks (holidays, year-end, etc.)
                if (weekBars.Count < 4)
                {
                    var tradingDates = weekBars.Select(b => b.Time.Date).ToList();
                    excludedWeeks.Add((weekStart, weekBars.Count, tradingDates));
                    continue;
                }
                
                var firstBar = weekBars.First();
                var lastBar = weekBars.Last();
                
                var weeklyBar = new TradeBar
                {
                    Time = firstBar.Time,
                    Symbol = firstBar.Symbol,
                    Open = firstBar.Open,
                    High = weekBars.Max(b => b.High),
                    Low = weekBars.Min(b => b.Low),
                    Close = lastBar.Close,
                    Volume = weekBars.Sum(b => b.Volume)
                };
                
                weeklyBars.Add(weeklyBar);
                // Store the trading dates for this weekly bar
                weeklyTradingDates[weeklyBar.Time] = weekBars.Select(b => b.Time.Date).ToList();
            }
            
            var history = weeklyBars;

            // Log summary statistics for quick reference
            if (history.Count > 0)
            {
                var avgClose = history.Average(b => b.Close);
                var maxClose = history.Max(b => b.Close);
                var minClose = history.Min(b => b.Close);
                
                Log($"Summary: {history.Count} weekly bars | Avg Close: ${avgClose:F2} | Range: ${minClose:F2} - ${maxClose:F2}");
            }
            
            // Create formatted table output with percentages
            Log($"Retrieved {history.Count} weekly bars for QQQ from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
            Log("");
            
            // Table header with percentage columns
            Log("Date       | Open      | High      | Low       | Close     | Volume      | %O->H    | %O->L    | %O->C");
            Log("-----------|-----------|-----------|-----------|-----------|-------------|----------|----------|----------");
            
            // Collect percentages for statistics
            var pctOpenToHighList = new List<decimal>();
            var pctOpenToLowList = new List<decimal>();
            var pctOpenToCloseList = new List<decimal>();
            
            // Table rows with percentages
            foreach (var bar in history)
            {
                // Calculate percentages from open
                var pctOpenToHigh = bar.Open != 0 ? (bar.High - bar.Open) / bar.Open * 100m : 0m;
                var pctOpenToLow = bar.Open != 0 ? (bar.Low - bar.Open) / bar.Open * 100m : 0m;
                var pctOpenToClose = bar.Open != 0 ? (bar.Close - bar.Open) / bar.Open * 100m : 0m;
                
                // Store for statistics
                pctOpenToHighList.Add(pctOpenToHigh);
                pctOpenToLowList.Add(pctOpenToLow);
                pctOpenToCloseList.Add(pctOpenToClose);
                
                Log($"{bar.Time:yyyy-MM-dd} | {bar.Open,9:F2} | {bar.High,9:F2} | {bar.Low,9:F2} | {bar.Close,9:F2} | {bar.Volume,11:N0} | {pctOpenToHigh,8:F2}% | {pctOpenToLow,8:F2}% | {pctOpenToClose,8:F2}%");
            }
            
            Log("");
            Log($"Total weekly bars: {history.Count}");
            Log("");
            
            // Log trading dates for each week for verification
            Log("Weekly Bar Trading Dates:");
            Log("========================");
            foreach (var bar in history.OrderBy(b => b.Time))
            {
                if (weeklyTradingDates.TryGetValue(bar.Time, out var tradingDates))
                {
                    var datesStr = string.Join(", ", tradingDates.Select(d => d.ToString("yyyy-MM-dd")));
                    Log($"Week starting {bar.Time:yyyy-MM-dd} ({tradingDates.Count} days): {datesStr}");
                }
            }
            Log("");
            
            // Calculate and log statistics for each percentage column
            Log("Percentage Statistics:");
            Log("====================");
            
            // Helper function to calculate median
            Func<List<decimal>, decimal> median = (list) =>
            {
                if (list.Count == 0) return 0m;
                var sorted = list.OrderBy(x => x).ToList();
                var mid = sorted.Count / 2;
                return sorted.Count % 2 == 0 
                    ? (sorted[mid - 1] + sorted[mid]) / 2m 
                    : sorted[mid];
            };
            
            // Helper function to calculate standard deviation
            Func<List<decimal>, decimal> stdDev = (list) =>
            {
                if (list.Count == 0) return 0m;
                var avg = list.Average();
                var sumSquaredDiffs = list.Sum(x => (x - avg) * (x - avg));
                return (decimal)Math.Sqrt((double)(sumSquaredDiffs / list.Count));
            };
            
            // %O->H Statistics
            var avgOtoH = pctOpenToHighList.Average();
            var medianOtoH = median(pctOpenToHighList);
            var stdDevOtoH = stdDev(pctOpenToHighList);
            Log($"%O->H: Average = {avgOtoH:F2}% | Median = {medianOtoH:F2}% | Std Dev = {stdDevOtoH:F2}%");
            
            // %O->L Statistics
            var avgOtoL = pctOpenToLowList.Average();
            var medianOtoL = median(pctOpenToLowList);
            var stdDevOtoL = stdDev(pctOpenToLowList);
            Log($"%O->L: Average = {avgOtoL:F2}% | Median = {medianOtoL:F2}% | Std Dev = {stdDevOtoL:F2}%");
            
            // %O->C Statistics
            var avgOtoC = pctOpenToCloseList.Average();
            var medianOtoC = median(pctOpenToCloseList);
            var stdDevOtoC = stdDev(pctOpenToCloseList);
            Log($"%O->C: Average = {avgOtoC:F2}% | Median = {medianOtoC:F2}% | Std Dev = {stdDevOtoC:F2}%");
            
            // Log excluded weeks (weeks with less than 4 trading days)
            if (excludedWeeks.Count > 0)
            {
                Log("");
                Log($"Excluded Weeks (less than 4 trading days): {excludedWeeks.Count}");
                Log("==========================================");
                foreach (var excluded in excludedWeeks.OrderBy(x => x.WeekStart))
                {
                    var datesStr = string.Join(", ", excluded.TradingDates.Select(d => d.ToString("yyyy-MM-dd")));
                    Log($"Week starting {excluded.WeekStart:yyyy-MM-dd} ({excluded.TradingDays} days): {datesStr}");
                }
            }
            else
            {
                Log("");
                Log("No weeks excluded (all weeks had 4+ trading days)");
            }
        }

        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// Slice object keyed by symbol containing the stock data
        public override void OnData(Slice data)
        {
            // Algorithm logic can go here
        }

    }
}
