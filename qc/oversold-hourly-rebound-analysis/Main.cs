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
    using QuantConnect.Storage;
    using QuantConnect.Statistics;
    using QCAlgorithmFramework = QuantConnect.Algorithm.QCAlgorithm;
    using QCAlgorithmFrameworkBridge = QuantConnect.Algorithm.QCAlgorithm;
    using Calendar = QuantConnect.Data.Consolidators.Calendar;
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Securities;

public class Oversold_Hourly_Rebound_Analysis_Algorithm : QCAlgorithm
{
    private string TICKER = "NVDA";
    private DateTime START_DATE;
    private decimal START_PRICE = 40.09m;

    private DateTime HISTORY_END_DATE;
    private Symbol TICKER_SYMBOL;

    public override void Initialize()
    {
        // -----------------------------
        // USER INPUTS
        // -----------------------------
        var startDateStr = "2023-06-26";

        try
        {
            START_DATE = DateTime.ParseExact(startDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            throw new Exception("ERROR: Could not parse START_DATE. Use yyyy-MM-dd.");
        }

        HISTORY_END_DATE = START_DATE.AddDays(40);

        // Start AFTER history window so QC allows History()
        var algoStartDate = HISTORY_END_DATE.AddDays(1);
        SetStartDate(algoStartDate);
        SetEndDate(algoStartDate.AddDays(5));

        // Add NVDA with split-adjusted prices (no dividend adjustment)
        var equity = AddEquity(TICKER, Resolution.Daily);
        equity.SetDataNormalizationMode(DataNormalizationMode.SplitAdjusted);
        TICKER_SYMBOL = equity.Symbol;

        // Run calculation
        CalculateEndDayRebounds();
    }

    private void CalculateEndDayRebounds()
    {
        var periods = new[] { 5, 10, 20 };

        // Pull daily bars for window
        var history = History<TradeBar>(TICKER_SYMBOL, START_DATE, HISTORY_END_DATE);

        if (history == null || !history.Any())
        {
            Debug("ERROR: No historical data returned.");
            return;
        }

        var dailyBars = history
            .OrderBy(bar => bar.Time)
            .ToList();

        // Bars strictly AFTER START_DATE (T+1, T+2, …)
        var futureBars = dailyBars
            .Where(bar => bar.Time.Date > START_DATE.Date)
            .ToList();

        if (!futureBars.Any())
        {
            Debug("ERROR: No future bars found after START_DATE.");
            return;
        }

        foreach (var days in periods)
        {
            int targetIndex = days - 1;

            if (futureBars.Count <= targetIndex)
            {
                Debug($"Not enough data for {days}D.");
                continue;
            }

            if (days == 20)
            {
                //
                // ---------- 20D TARGET-DAY REBOUND ----------
                //
                var endDayBar = futureBars[targetIndex];
                var high20 = endDayBar.High;
                var rebound20 = (high20 - START_PRICE) / START_PRICE * 100m;

                Debug($"20D Rebound %: {rebound20:F2}");

                //
                // ---------- 20D PEAK REBOUND ----------
                //
                var bars20 = futureBars.Take(targetIndex + 1).ToList();

                decimal peakHigh = decimal.MinValue;
                int daysToPeak = 0;

                for (int i = 0; i < bars20.Count; i++)
                {
                    if (bars20[i].High > peakHigh)
                    {
                        peakHigh = bars20[i].High;
                        daysToPeak = i + 1;  // T+1 is index 0
                    }
                }

                var peakRebound = (peakHigh - START_PRICE) / START_PRICE * 100m;

                Debug($"20D Peak Rebound %: {peakRebound:F2}");
                Debug($"Days to 20D Peak: {daysToPeak}");
            }
            else
            {
                //
                // ---------- 5D & 10D REBOUND ----------
                //
                var endBar = futureBars[targetIndex];
                var high = endBar.High;
                var reboundPercent = (high - START_PRICE) / START_PRICE * 100m;

                Debug($"{days}D Rebound %: {reboundPercent:F2}");
            }
        }
    }

    public override void OnData(Slice data)
    {
        // No live logic
    }
}
