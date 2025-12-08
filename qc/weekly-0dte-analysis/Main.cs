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
        private const bool LogWeeklyTradingDates = false; // Set to true to log individual trading dates for each week
        private const int MinTradingDays = 4;

        private const string TickerSymbol = "QQQ";
        private static readonly DateTime HistoryStartDate = new DateTime(2015, 1, 1);
        private static readonly DateTime HistoryEndDate = new DateTime(2025, 12, 1);

        public override void Initialize()
        {
            // NOTE: Algorithm start/end dates are the dates of the algorithm execution, not the data retrieval.
            //       We don't really care about these dates as long as they're after the data retrieval end date since we can't retrieve history about the future
            SetStartDate(HistoryEndDate.AddDays(1)); // Set Start Date (1 day after the history end date)
            SetEndDate(HistoryEndDate.AddDays(2)); // Set End Date (2 days after the history end date)

            // Add with daily resolution (we'll consolidate to weekly)
            // Set data normalization to split-adjusted to account for stock splits
            var equity = AddEquity(TickerSymbol, Resolution.Daily);
            equity.SetDataNormalizationMode(DataNormalizationMode.SplitAdjusted);

            var dailyHistory = History(equity.Symbol, HistoryStartDate, HistoryEndDate, Resolution.Daily);

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
                if (weekBars.Count < MinTradingDays)
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
            Log($"Retrieved {history.Count} weekly bars for {TickerSymbol} from {HistoryStartDate:yyyy-MM-dd} to {HistoryEndDate:yyyy-MM-dd}");
            Log("");
            
            // Table header with percentage columns
            Log("StartDate  | EndDate   | Open      | High      | Low       | Close     | Volume      | TradingDays  | OpenToHigh | OpenToLow  | OpenToClose | LowToHigh");
            Log("-----------|-----------|-----------|-----------|-----------|-----------|-------------|--------------|------------|------------|-------------|----------");
            
            // Collect percentages for statistics
            var pctOpenToHighList = new List<decimal>();
            var pctOpenToLowList = new List<decimal>();
            var pctOpenToCloseList = new List<decimal>();
            var pctLowToHighList = new List<decimal>();
            
            // Table rows with percentages
            foreach (var bar in history)
            {
                // Calculate percentages
                var pctOpenToHigh = bar.Open != 0 ? (bar.High - bar.Open) / bar.Open * 100m : 0m;
                var pctOpenToLow = bar.Open != 0 ? (bar.Low - bar.Open) / bar.Open * 100m : 0m;
                var pctOpenToClose = bar.Open != 0 ? (bar.Close - bar.Open) / bar.Open * 100m : 0m;
                var pctLowToHigh = bar.Low != 0 ? (bar.High - bar.Low) / bar.Low * 100m : 0m;
                
                // Get trading days count and end date for this week
                var tradingDaysCount = 0;
                var weekEndDate = bar.Time;
                if (weeklyTradingDates.TryGetValue(bar.Time, out var tradingDates))
                {
                    tradingDaysCount = tradingDates.Count;
                    weekEndDate = tradingDates.Last(); // Last trading day of the week
                }
                
                // Store for statistics
                pctOpenToHighList.Add(pctOpenToHigh);
                pctOpenToLowList.Add(pctOpenToLow);
                pctOpenToCloseList.Add(pctOpenToClose);
                pctLowToHighList.Add(pctLowToHigh);
                
                Log($"{bar.Time:yyyy-MM-dd} | {weekEndDate:yyyy-MM-dd} | {bar.Open,9:F2} | {bar.High,9:F2} | {bar.Low,9:F2} | {bar.Close,9:F2} | {bar.Volume,11:N0} | {tradingDaysCount,13} | {pctOpenToHigh,8:F2}% | {pctOpenToLow,8:F2}% | {pctOpenToClose,8:F2}% | {pctLowToHigh,8:F2}%");
            }
            
            Log("");
            Log($"Total weekly bars: {history.Count}");
            Log("");
            
            // Log trading dates for each week for verification (if enabled)
            if (LogWeeklyTradingDates)
            {
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
            }
            
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
            
            // OpenToHigh Statistics
            var avgOtoH = pctOpenToHighList.Average();
            var medianOtoH = median(pctOpenToHighList);
            var stdDevOtoH = stdDev(pctOpenToHighList);
            Log($"OpenToHigh: Average = {avgOtoH:F2}% | Median = {medianOtoH:F2}% | Std Dev = {stdDevOtoH:F2}%");
            
            // OpenToLow Statistics
            var avgOtoL = pctOpenToLowList.Average();
            var medianOtoL = median(pctOpenToLowList);
            var stdDevOtoL = stdDev(pctOpenToLowList);
            Log($"OpenToLow: Average = {avgOtoL:F2}% | Median = {medianOtoL:F2}% | Std Dev = {stdDevOtoL:F2}%");
            
            // OpenToClose Statistics
            var avgOtoC = pctOpenToCloseList.Average();
            var medianOtoC = median(pctOpenToCloseList);
            var stdDevOtoC = stdDev(pctOpenToCloseList);
            Log($"OpenToClose: Average = {avgOtoC:F2}% | Median = {medianOtoC:F2}% | Std Dev = {stdDevOtoC:F2}%");
            
            // LowToHigh Statistics
            var avgLtoH = pctLowToHighList.Average();
            var medianLtoH = median(pctLowToHighList);
            var stdDevLtoH = stdDev(pctLowToHighList);
            Log($"LowToHigh: Average = {avgLtoH:F2}% | Median = {medianLtoH:F2}% | Std Dev = {stdDevLtoH:F2}%");
            
            // Log excluded weeks (weeks with less than 4 trading days)
            if (excludedWeeks.Count > 0)
            {
                Log("");
                Log($"Excluded Weeks (less than {MinTradingDays} trading days): {excludedWeeks.Count}");
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
                Log($"No weeks excluded (all weeks had {MinTradingDays}+ trading days)");
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
