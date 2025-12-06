#region imports
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Drawing;

using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Risk;

using QuantConnect.Api;
using QuantConnect.Parameters;
using QuantConnect.Benchmarks;
using QuantConnect.Brokerages;
using QuantConnect.Commands;
using QuantConnect.Configuration;
using QuantConnect.Util;
using QuantConnect.Interfaces;

using QuantConnect.Indicators;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Data.Custom;
using QuantConnect.Data.Consolidators;

using QuantConnect.Notifications;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Fills;
using QuantConnect.Orders.Slippage;
using QuantConnect.Orders.TimeInForces;

using QuantConnect.Scheduling;
using QuantConnect.Securities;
using QuantConnect.Securities.Equity;
using QuantConnect.Securities.Future;
using QuantConnect.Securities.Forex;
using QuantConnect.Securities.Option;
using QuantConnect.Securities.Positions;
using QuantConnect.Securities.Crypto;
using QuantConnect.Securities.CryptoFuture;
using QuantConnect.Securities.IndexOption;
using QuantConnect.Securities.Interfaces;
using QuantConnect.Securities.Volatility;

using QuantConnect.Storage;
using QuantConnect.Statistics;
#endregion

using System.Linq;
using QuantConnect.Data.Market;

// ============================================================================
// FULL WORKING ALGORITHM (NVDA + new inputs, using Log() for clean output)
// ============================================================================

public class Oversold_Daily_Rebound_Analysis_Algorithm : QCAlgorithm
{
    private string TICKER = "NVDA";
    private Symbol TICKER_SYMBOL;

    private class ReboundInput
    {
        public DateTime StartDate;
        public decimal StartPrice;
    }

    private class ResultRow
    {
        public DateTime StartDate;
        public decimal StartPrice;
        public decimal? R5;
        public decimal? R10;
        public decimal? R20;
        public decimal? R20Peak;
        public int? DaysToPeak;
    }

    private List<ReboundInput> _inputs;
    private List<ResultRow> _results;
    private DateTime _earliestStart;
    private DateTime _latestEnd;

    public override void Initialize()
    {
        // ==============================
        // INPUTS (Date + Price)
        // ==============================
        _inputs = new List<ReboundInput>
        {
            new ReboundInput { StartDate = Parse("9/21/2023"), StartPrice = 41.00m },
            new ReboundInput { StartDate = Parse("4/19/2024"), StartPrice = 75.60m },
            new ReboundInput { StartDate = Parse("8/5/2024"),  StartPrice = 90.78m },
            new ReboundInput { StartDate = Parse("1/27/2025"), StartPrice = 116.69m },
            new ReboundInput { StartDate = Parse("3/10/2025"), StartPrice = 105.42m },
            new ReboundInput { StartDate = Parse("4/7/2025"),  StartPrice = 88.77m }
        };

        _results = new List<ResultRow>();

        _earliestStart = _inputs.Min(i => i.StartDate);
        _latestEnd = _inputs.Max(i => i.StartDate.AddDays(40));

        SetStartDate(_latestEnd.AddDays(1));
        SetEndDate(_latestEnd.AddDays(5));

        // Add NVDA with split adjustment (NO dividend adjustment)
        var equity = AddEquity(TICKER, Resolution.Daily);
        equity.SetDataNormalizationMode(DataNormalizationMode.SplitAdjusted);

        TICKER_SYMBOL = equity.Symbol;

        RunAnalysis();
    }

    private void RunAnalysis()
    {
        var fullHistory = History<TradeBar>(TICKER_SYMBOL, _earliestStart, _latestEnd)
            .OrderBy(b => b.Time)
            .ToList();

        foreach (var input in _inputs)
        {
            var start = input.StartDate;
            var startPrice = input.StartPrice;

            var windowBars = fullHistory
                .Where(b => b.Time.Date >= start.Date && b.Time.Date <= start.AddDays(40).Date)
                .ToList();

            var futureBars = windowBars
                .Where(b => b.Time.Date > start.Date)
                .ToList();

            var row = new ResultRow
            {
                StartDate = start,
                StartPrice = startPrice
            };

            if (futureBars.Count >= 5)
                row.R5 = Pct(futureBars[4].High, startPrice);

            if (futureBars.Count >= 10)
                row.R10 = Pct(futureBars[9].High, startPrice);

            if (futureBars.Count >= 20)
            {
                row.R20 = Pct(futureBars[19].High, startPrice);

                decimal peakHigh = decimal.MinValue;
                int peakIdx = 0;

                for (int i = 0; i < 20; i++)
                {
                    if (futureBars[i].High > peakHigh)
                    {
                        peakHigh = futureBars[i].High;
                        peakIdx = i + 1;
                    }
                }

                row.R20Peak = Pct(peakHigh, startPrice);
                row.DaysToPeak = peakIdx;
            }

            _results.Add(row);
        }

        // === CLEAN CSV OUTPUT ===
        Log("StartDate,StartPrice,5D,10D,20D,20DPeak,PeakDays");

        foreach (var r in _results)
        {
            Log($"{r.StartDate:MM/dd/yyyy},{r.StartPrice:F2},{Fmt(r.R5)},{Fmt(r.R10)},{Fmt(r.R20)},{Fmt(r.R20Peak)},{FmtDays(r.DaysToPeak)}");
        }
    }

    private decimal Pct(decimal high, decimal start) =>
        (high - start) / start * 100m;

    private string Fmt(decimal? v) =>
        v.HasValue ? v.Value.ToString("F2") : "NA";

    private string FmtDays(int? v) =>
        v.HasValue ? v.Value.ToString() : "NA";

    private DateTime Parse(string s) =>
        DateTime.ParseExact(s, "M/d/yyyy", CultureInfo.InvariantCulture);

    public override void OnData(Slice data) { }
}
