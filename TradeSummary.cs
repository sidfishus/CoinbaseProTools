using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CBProductTrade = CoinbasePro.Services.Products.Models.ProductTrade;
using OrderSide = CoinbasePro.Services.Orders.Types.OrderSide;
using TradeSummaryLevels = System.Collections.Generic.Dictionary<int /* Summary period in units */, CoinbaseProToolsForm.TradeSummaryState>;

namespace CoinbaseProToolsForm
{

	public class TradeSummaryState
	{
		public TradeSummaryState previous;
		public decimal buyTotal;
		public decimal sellTotal;
		public DateTimeOffset timeStart;
		public int numBuys;
		public int numSells;
		public decimal totalSellPrice; // For working out avgs
		public decimal totalBuyPrice;
		public decimal highSellPrice;
		public decimal highBuyPrice;
		public decimal lowSellPrice;
		public decimal lowBuyPrice;
		public decimal highPrice
		{
			get
			{
				return Math.Max(highSellPrice,highBuyPrice);
			}
		}
		public decimal lowPrice
		{
			get
			{
				if (lowSellPrice == 0) return lowBuyPrice;
				else if (lowBuyPrice == 0) return lowSellPrice;
				return Math.Min(lowSellPrice, lowBuyPrice);
			}
		}
	}

	public static class TradeSummary
	{
		public const int singleUnitOfTimeSecs = 30;
		public const int summaryPeriodGrowFactor = 3;
		public const int numLevels = 8; /* From 30 seconds up to approx 18 hours */
		public const int feedOutputSummaryLevel = 3;

		public static void IncrementSummary(TradeSummaryState summary, CBProductTrade trade)
		{
			// This is weird. Trades that come in to here as 'buy' actually show as 'sell' on Coinbase.
			if (trade.Side == OrderSide.Sell)
			{
				summary.buyTotal += trade.Size;
				++summary.numBuys;
				summary.totalBuyPrice += trade.Price;

				if (summary.lowBuyPrice == 0) summary.lowBuyPrice = trade.Price;
				else summary.lowBuyPrice = Math.Min(summary.lowBuyPrice, trade.Price);

				summary.highBuyPrice = Math.Max(summary.highBuyPrice, trade.Price);
			}
			else
			{
				summary.sellTotal += trade.Size;
				++summary.numSells;
				summary.totalSellPrice += trade.Price;

				if (summary.lowSellPrice == 0) summary.lowSellPrice = trade.Price;
				else summary.lowSellPrice = Math.Min(summary.lowSellPrice, trade.Price);

				summary.highSellPrice = Math.Max(summary.highSellPrice, trade.Price);
			}
		}

		public static int NumTrades(TradeSummaryState ts)
		{
			return ts.numBuys + ts.numSells;
		}

		public static int NumTradeDifference(TradeSummaryState ts)
		{
			return ts.numBuys - ts.numSells;
		}

		public static TradeSummaryState GetEarliestSummary(TradeSummaryState tradeSummary)
		{
			var earliest = tradeSummary;

			for (; earliest.previous != null;)
			{
				earliest = earliest.previous;
			}

			return earliest;
		}

		public static int TotalVolume(TradeSummaryState ts)
		{
			return ts.numBuys + ts.numSells;
		}

		public static TradeSummaryState GetTradeSummary(int unitsOfTime, TradeSummaryLevels levels)
		{
			var summaryRv = new TradeSummaryState();
			TradeSummaryState iterSummary;
			if (levels.TryGetValue(1, out iterSummary))
			{

				summaryRv.timeStart = iterSummary.timeStart;
				for (int i = 1; i <= unitsOfTime && iterSummary != null; ++i)
				{
					IncrementSummary(summaryRv, iterSummary);
					summaryRv.timeStart = summaryRv.timeStart.AddSeconds(-singleUnitOfTimeSecs);
					iterSummary = iterSummary.previous;
				}
			}
			return summaryRv;
		}

		public static int SummaryPeriodTotalTimeSecs(int level)
		{
			var summaryPeriodTotalTimeSeconds = (level * TradeSummary.singleUnitOfTimeSecs);
			return summaryPeriodTotalTimeSeconds;
		}

		public static TradeSummaryState CreateTradeSummary(
			TradeSummaryState tradeSummary, DateTimeOffset nextPeriodStart, int howFarToGoBack= summaryPeriodGrowFactor)
		{
			var summary = new TradeSummaryState();
			var iterSummary = tradeSummary;

			for (int i = 0; i < howFarToGoBack;)
			{
				if (iterSummary.timeStart < nextPeriodStart)
				{
					IncrementSummary(summary, iterSummary);
					++i;
				}

				iterSummary = iterSummary.previous;
			}

			return summary;
		}

		public static void IncrementSummary(TradeSummaryState lhs, TradeSummaryState rhs)
		{
			lhs.buyTotal += rhs.buyTotal;
			lhs.sellTotal += rhs.sellTotal;

			lhs.numBuys += rhs.numBuys;
			lhs.numSells += rhs.numSells;

			lhs.totalBuyPrice += rhs.totalBuyPrice;
			lhs.totalSellPrice += rhs.totalSellPrice;

			if (rhs.lowBuyPrice > 0)
			{
				if (lhs.lowBuyPrice > 0) lhs.lowBuyPrice = Math.Min(lhs.lowBuyPrice, rhs.lowBuyPrice);
				else lhs.lowBuyPrice = rhs.lowBuyPrice;
			}

			if (rhs.lowSellPrice > 0)
			{
				if (lhs.lowSellPrice > 0) lhs.lowSellPrice = Math.Min(lhs.lowSellPrice, rhs.lowSellPrice);
				else lhs.lowSellPrice = rhs.lowSellPrice;
			}

			lhs.highBuyPrice = Math.Max(lhs.highBuyPrice, rhs.highBuyPrice);
			lhs.highSellPrice = Math.Max(lhs.highSellPrice, rhs.highSellPrice);
		}

		public static DateTimeOffset TradeSummaryEndTime(int level, TradeSummaryState summary)
		{
			return summary.timeStart.AddSeconds(singleUnitOfTimeSecs * level);
		}

		// Difference between buy and sell.
		public static decimal TradeDifference(TradeSummaryState ts)
		{
			return ts.buyTotal - ts.sellTotal;
		}

		public static decimal AvgPrice(TradeSummaryState summary)
		{
			if (summary.numSells == 0 && summary.numBuys == 0) return 0;

			decimal avgSellPrice = ((summary.numSells == 0) ? 0 : summary.totalSellPrice / summary.numSells);
			decimal avgBuyPrice = ((summary.numBuys == 0) ? 0 : summary.totalBuyPrice / summary.numBuys);

			if (avgSellPrice != 0 && avgBuyPrice != 0) return (avgSellPrice + avgBuyPrice) / 2;

			else if (avgSellPrice != 0) return avgSellPrice;
			else return avgBuyPrice;
		}

		public static TradeSummaryState FirstNonZeroSummary(TradeSummaryState iterSummary)
		{

			bool foundOne;
			do
			{
				foundOne = !(iterSummary.numBuys == 0 && iterSummary.numSells == 0);
				if (!foundOne)
				{
					iterSummary = iterSummary.previous;
				}
			} while (!foundOne && iterSummary != null);

			return iterSummary;
		}
	}
}
