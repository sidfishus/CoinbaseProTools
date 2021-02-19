using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CBProductTrade = CoinbasePro.Services.Products.Models.ProductTrade;
using CoinbasePro;
using CBPagedTradeList = System.Collections.Generic.IList<System.Collections.Generic.IList<CoinbasePro.Services.Products.Models.ProductTrade>>;
using Debug = System.Diagnostics.Debug;
using TradeSummaryLevels = System.Collections.Generic.Dictionary<int /* Summary period in units */, CoinbaseProToolsForm.TradeSummaryState>;
using EventOutputter = System.Action<System.Collections.Generic.IEnumerable<System.Tuple<string, System.DateTimeOffset?>>>;
using EventOutput = System.Tuple<string, System.DateTimeOffset?>;
using SummaryLevelUpdateCallback = System.Action<System.Collections.Generic.Dictionary<int /* Summary period in units */, CoinbaseProToolsForm.TradeSummaryState>>;
using NewTradesCallback = System.Action<System.Collections.Generic.Dictionary<int /* Summary period in units */,
	CoinbaseProToolsForm.TradeSummaryState>,
	System.Collections.Generic.List<CoinbasePro.Services.Products.Models.ProductTrade>>;
using ProductType = CoinbasePro.Shared.Types.ProductType;
using ProductStats = CoinbasePro.Services.Products.Types.ProductStats;
using ProductStatsDictionary = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType, CoinbasePro.Services.Products.Types.ProductStats>;
using OrderSide = CoinbasePro.Services.Orders.Types.OrderSide;
using CoinbasePro.Services.Products.Models;

namespace CoinbaseProToolsForm
{
	public class TradeHistoryState
	{
		public int latestTradeId=0;
		public TradeSummaryState inProgressTradeSummary; // A trade summary which is not complete yet
		public TradeSummaryLevels tradeSummaries=new TradeSummaryLevels();
		public List<CBProductTrade> tradesFromTheLast5Mins;
		public readonly object historyLock = new object();
	}

	class CompareCBProductTradeByTimeAscending : IComparer<CBProductTrade>
	{
		int IComparer<CBProductTrade>.Compare(CBProductTrade x, CBProductTrade y)
		{
			int rv = x.Time.CompareTo(y.Time);
			if (rv == 0) rv = x.TradeId.CompareTo(y.TradeId);
			return rv;
		}
	}

	public static class TradeHistory
	{

		private static async Task<CBPagedTradeList> LoadTrades(int numPages, Action<Exception> WriteExceptions,
			CoinbaseProClient cbClient, ProductType product)
		{
			CBPagedTradeList trades = null;
			while (trades == null)
			{
				try
				{
					trades = await cbClient.ProductsService.GetTradesAsync(product,
						100, numPages);
				}
				catch (Exception e)
				{
					WriteExceptions(e);
				}
			}
			return trades;
		}

		public static async Task LoadApproxLast24Hour(CoinbaseProClient cbClient, TradeHistoryState tradeHistory,
			Action<Exception> WriteExceptions, EventOutputter Output, Action RunUponCompletion,
			Func<ProductStatsDictionary> getProductStats, ProductType productType,
			Func<ProductType> getActiveProduct)
		{
			var prodInfo = Products.productInfo[productType];
#if DEBUG
			int numPagesForApprox24Hours = (int)(prodInfo.numPagesForApprox24Hour/4); // An hour or so.
			//int numPagesForApprox24Hours = prodInfo.numPagesForApprox24Hour; // Approx one day
#else
			int numPagesForApprox24Hours = prodInfo.numPagesForApprox24Hour; // Approx one day
#endif
			//const int numPagesForApprox24Hours = 120; //Approx 3 days
			//const int numPagesForApprox24Hours = 240; //Approx 6 days
			//const int numPagesForApprox24Hours = 500; //Approx 12 days

			CBPagedTradeList trades = await LoadTrades(numPagesForApprox24Hours, WriteExceptions,
				cbClient, productType);

			// Process the trades
			//sidtodo this is swallowing exceptions/????

			var prodstatsDictionary = getProductStats();
			ProductStats prodStats = null;
			if (prodstatsDictionary != null)
			{
				prodstatsDictionary.TryGetValue(productType, out prodStats);
			}

			ConditionalAddTrades(trades, tradeHistory, ((productType == getActiveProduct())?Output:null),
				prodStats, null, null, productType);

			RunUponCompletion?.Invoke();
		}

		// Returns true if we iterated to the end
		private static bool IterateCBPagedTradeList(CBPagedTradeList trades,
			bool ascending,
			int pageStartIdx, Func<int,int> tradeStartIdx,Func<CBProductTrade, int, int, bool> callback)
		{

			if (ascending)
			{
				for (int pageIdx = pageStartIdx; pageIdx < trades.Count; ++pageIdx)
				{
					var iterPage = trades[pageIdx];

					var beginTradeIdx = ((pageIdx == pageStartIdx) ? tradeStartIdx(iterPage.Count) : 0);
					for (int tradeIdx = beginTradeIdx; tradeIdx < iterPage.Count; ++tradeIdx)
					{
						if (!callback(iterPage[tradeIdx], pageIdx, tradeIdx))
						{
							return false;
						}
					}
				}
			}
			else
			{
				pageStartIdx = (pageStartIdx == -1 ?trades.Count-1: pageStartIdx);
				for (int pageIdx = pageStartIdx; pageIdx >= 0; --pageIdx)
				{
					var iterTradePage = trades[pageIdx];

					int beginTradeIdx;
					if (pageIdx == pageStartIdx)
					{
						beginTradeIdx = tradeStartIdx(iterTradePage.Count);
					}
					else
					{
						beginTradeIdx = iterTradePage.Count - 1;
					}

					for (int tradeIdx = beginTradeIdx; tradeIdx >= 0; --tradeIdx)
					{
						if (!callback(iterTradePage[tradeIdx], pageIdx, tradeIdx))
						{
							return false;
						}
					}
				}
			}

			return true;
		}

		//sidtodo take in to account we may already have these trades. i.e. for historic data.
		// Returns true if the data was added, or false if we don't have the latest trade in this list and therefore
		// need to go and load more.
		public static bool ConditionalAddTrades(CBPagedTradeList trades,
			TradeHistoryState tradeHistory, EventOutputter Output,
			ProductStats productStats,
			SummaryLevelUpdateCallback summaryLevelUpdateCallback,
			NewTradesCallback newTradesCallback, ProductType product)
		{

			List<EventOutput> optionalEventOutputText = null;
			bool shouldOutput = (Output != null);

			if (trades == null || trades.Count() == 0 || trades[0].Count() == 0)
			{
				return false;
			}

			int latestTradeIdInThisDataset = trades[0][0].TradeId;

			int firstNewTradePage = -1;
			int firstNewTradeIdx = -1;

			if (tradeHistory.latestTradeId != 0)
			{
				if (latestTradeIdInThisDataset == tradeHistory.latestTradeId)
				{
					// Already up to date.
					return true;
				}

				// Find the last trade we have (latestTradeId) in the list
				// Iterate the trades ascending
				bool foundPreviousTrade = !IterateCBPagedTradeList(trades, true, 0, (unused) => 1,
					(iterTrade, pageIdx, tradeIdx) =>
					{
						if (iterTrade.TradeId == tradeHistory.latestTradeId)
						{
							AddCBPagedProductListIndexes(trades, pageIdx, tradeIdx, -1 /* We want the earlier one */,
								out firstNewTradePage,
								out firstNewTradeIdx);
							return false;
						}
						return true;
					});

				if (!foundPreviousTrade)
				{
					// Go and load more data.
					return false;
				}
			}
			else
			{
				// Get the date of the oldest trade and add one minute, as well as clearing the seconds.
				// And then find the first trade which is newer or equal to that time.
				// This will give us a clean time to begin holding data
				tradeHistory.inProgressTradeSummary = new TradeSummaryState();

				if (!GetHistoryBeginInfo(tradeHistory, trades, out tradeHistory.inProgressTradeSummary.timeStart,
					out firstNewTradePage, out firstNewTradeIdx))
				{
					// Highly unlikely - not enough trades to work with.
					return false;
				}
			}

			var productInfo = Products.productInfo[product];

			tradeHistory.latestTradeId = latestTradeIdInThisDataset;

			Debug.Assert(firstNewTradePage != -1);
			Debug.Assert(firstNewTradeIdx != -1);

			// Important this is done in a loop as we may need to call the callback multiple times.
			bool isFirstAddTradeIteration = true;
			for(int outerPageIdx= firstNewTradePage, outerTradeIdx=firstNewTradeIdx; ; )
			{
				bool finishedIterating=false;
				TradeSummaryLevels slCopiesForCallback = null;
				List<CBProductTrade> recentTradesCopyForCallback = null;
				lock (tradeHistory.historyLock)
				{

					if (isFirstAddTradeIteration)
					{
						UpdateRecentTrades(tradeHistory, trades, firstNewTradePage, firstNewTradeIdx);
						recentTradesCopyForCallback = new List<CBProductTrade>(tradeHistory.tradesFromTheLast5Mins);
					}

					// Add the new trades
					finishedIterating = IterateCBPagedTradeList(trades, false, outerPageIdx, (unused) => outerTradeIdx,
						(trade, innerPageIdx, innerTradeIdx) =>
						{
							bool summaryLevelsWereUpdated = AddNewTrade(tradeHistory, trade, innerPageIdx, innerTradeIdx,
								trades, productStats, shouldOutput, productInfo, ref optionalEventOutputText);

							bool stopForCallback = (summaryLevelsWereUpdated && summaryLevelUpdateCallback != null);

							if (stopForCallback)
							{
								outerPageIdx = innerPageIdx;
								outerTradeIdx = innerTradeIdx;

								// Copy the summary levels
								// This isn't a deep deep copy, it preserves the trade watch data as it is now.
								// Changes to the trade summary dictionary in THIS thread will not be shown in the callback,
								// but the actual summary value objects cannot be updated.
								// This is a performance enhancement - don't fully deep copy the trade summaries as it is not
								// necessary
								slCopiesForCallback=new TradeSummaryLevels(tradeHistory.tradeSummaries);
							}

							return !stopForCallback;
						});

					if (!finishedIterating)
					{
						finishedIterating = outerPageIdx == 0 && outerTradeIdx == 0;
					}
				}

				if (isFirstAddTradeIteration && newTradesCallback != null)
				{
					isFirstAddTradeIteration = false;
					newTradesCallback(slCopiesForCallback, recentTradesCopyForCallback);
				}

				if (slCopiesForCallback != null)
				{
					summaryLevelUpdateCallback(slCopiesForCallback);
				}

				if (finishedIterating)
				{
					break;
				}
			}

			if (optionalEventOutputText != null)
			{
				Output(optionalEventOutputText);
			}

			return true;
		}

		private static void UpdateRecentTrades(TradeHistoryState tradeHistory,
			CBPagedTradeList trades, int firstNewTradePage, int firstNewTradeIdx)
		{
			var nowMinus5Mins = DateTime.Now.AddSeconds(- (60*5));

			var previousList = tradeHistory.tradesFromTheLast5Mins;
			tradeHistory.tradesFromTheLast5Mins = null;

			IterateCBPagedTradeList(trades, false, firstNewTradePage, (unused) => firstNewTradeIdx,
				(trade, pageIdx, tradeIdx) =>
				{
					if (trade.Time >= nowMinus5Mins)
					{
						if (tradeHistory.tradesFromTheLast5Mins == null)
						{
							tradeHistory.tradesFromTheLast5Mins = new List<CBProductTrade>();
						}

						tradeHistory.tradesFromTheLast5Mins.Add(trade);
					}
					return true;
				});

			if (previousList != null)
			{
				for (int i = 0; i < previousList.Count; ++i)
				{
					var trade = previousList[i];
					if (trade.Time >= nowMinus5Mins)
					{
						if (tradeHistory.tradesFromTheLast5Mins == null)
						{
							tradeHistory.tradesFromTheLast5Mins = new List<CBProductTrade>();
						}

						tradeHistory.tradesFromTheLast5Mins.Add(trade);
					}
				}

				previousList.Clear();
			}

			if (tradeHistory.tradesFromTheLast5Mins != null)
			{
				// Sort them age descending
				tradeHistory.tradesFromTheLast5Mins.Sort(new CompareCBProductTradeByTimeAscending());
#if DEBUG
				if (tradeHistory.tradesFromTheLast5Mins.Count > 1)
				{
					// Test there are no duplicates & they are ordered correctly
					for (int i = 0; i < (tradeHistory.tradesFromTheLast5Mins.Count-1); ++i)
					{
						var outerTrade = tradeHistory.tradesFromTheLast5Mins[i];
						for (int ii = i+1; ii < tradeHistory.tradesFromTheLast5Mins.Count; ++ii)
						{
							var innerTrade= tradeHistory.tradesFromTheLast5Mins[ii];
							// Trades are ordered in ascending
							Debug.Assert(outerTrade.TradeId < innerTrade.TradeId);
							Debug.Assert(outerTrade.Time <= innerTrade.Time);
						}
					}
				}
#endif
			}
		}

		// Returns true if the levels were updated
		private static bool AddNewTrade(TradeHistoryState tradeHistory, CBProductTrade trade,
			int pageIdx, int tradeIdx, CBPagedTradeList trades,
			ProductStats productStats,
			bool shouldOutput, ProductInfo productInfo,
			ref List<EventOutput> optionalEventOutputText)
		{

			bool summaryLevelsUpdated = false;

			// Iterate the periods
			for (int depth = 1, level = 1; depth <= TradeSummary.numLevels;
				++depth, level *= TradeSummary.summaryPeriodGrowFactor)
			{

				// The previous trade summary at this level
				TradeSummaryState previousSummary;
				tradeHistory.tradeSummaries.TryGetValue(level, out previousSummary);

				// The total amount of seconds for a single unit at this level
				var summaryPeriodTotalTimeSeconds = TradeSummary.SummaryPeriodTotalTimeSecs(level);

				DateTimeOffset startTime;
				if (level == 1)
				{
					startTime = tradeHistory.inProgressTradeSummary.timeStart;
				}
				else
				{
					if (previousSummary == null)
					{
						// The previous level
						var previousLevel = level / TradeSummary.summaryPeriodGrowFactor;

						// The start time is the start time of the first summary of the previous level
						startTime = TradeSummary.GetEarliestSummary(tradeHistory.tradeSummaries[previousLevel]).timeStart;
					}
					else
					{
						startTime = previousSummary.timeStart.AddSeconds(summaryPeriodTotalTimeSeconds);
					}
				}

				// (At this level)
				int numSecondsPassedSinceLastSummary = (int)(trade.Time - startTime).TotalSeconds;
				int unitsOfTimePassed = numSecondsPassedSinceLastSummary / summaryPeriodTotalTimeSeconds;
				if (unitsOfTimePassed == 0)
				{
					// No more summary changes.
					break;
				}

				summaryLevelsUpdated = true;

				// Create the summary records for the units of time that have passed, including any blank ones
				for (int i = 1; i <= unitsOfTimePassed; ++i)
				{
					TradeSummaryState tradeSummary;
					if (level == 1)
					{
						tradeSummary = tradeHistory.inProgressTradeSummary;
						// Reset the inprogress summary
						tradeHistory.inProgressTradeSummary = new TradeSummaryState();
						tradeHistory.inProgressTradeSummary.timeStart = tradeSummary.timeStart.AddSeconds(summaryPeriodTotalTimeSeconds);
					}
					else
					{
						if (i == 1)
						{
							var previousLevel = level / TradeSummary.summaryPeriodGrowFactor;
							tradeSummary = TradeSummary.CreateTradeSummary(tradeHistory.tradeSummaries[previousLevel],
								startTime.AddSeconds(summaryPeriodTotalTimeSeconds));
						}
						else
						{
							// A blank summary
							tradeSummary = new TradeSummaryState();
						}

						if (previousSummary == null)
						{
							tradeSummary.timeStart = startTime;
						}
						else
						{
							tradeSummary.timeStart = previousSummary.timeStart.AddSeconds(summaryPeriodTotalTimeSeconds);
						}
					}

					tradeSummary.previous = previousSummary;

					previousSummary = tradeSummary;
					
					if (level == TradeSummary.feedOutputSummaryLevel &&
						shouldOutput && (tradeSummary.numBuys>0 || tradeSummary.numSells>0))
					{
						if (optionalEventOutputText == null)
						{
							optionalEventOutputText = new List<EventOutput>();
						}
						optionalEventOutputText.Add(
							new EventOutput(DescribeTradeSummary(tradeSummary, level, productStats, productInfo, true),
								TradeSummary.TradeSummaryEndTime(level, tradeSummary)));
					}
				}

				tradeHistory.tradeSummaries[level] = previousSummary;
			}

			// Add to the inprogress summary.
			TradeSummary.IncrementSummary(tradeHistory.inProgressTradeSummary, trade);

			return summaryLevelsUpdated;
		}

		public static string DescribeTradeSummary(TradeSummaryState tradeSummary, int level,
			ProductStats productStats, ProductInfo productInfo, bool extended=false)
		{
#if DEBUG
			if (productInfo.productType != ProductType.LinkGbp)
			{
			}
#endif
			decimal volume = tradeSummary.buyTotal + tradeSummary.sellTotal;
			//sidtodo previous
			//sidtodo ratio
			//sidtodo difference.
			// Volume ratio
			decimal volRatio;

			if (tradeSummary.sellTotal == tradeSummary.buyTotal)
			{
				volRatio = 0;
			}
			else if (tradeSummary.sellTotal > tradeSummary.buyTotal)
			{
				volRatio = -Decimal.Round(tradeSummary.sellTotal / volume, 2);
			}
			else
			{
				volRatio = Decimal.Round(tradeSummary.buyTotal / volume, 2);
			}

			decimal avgPrice = TradeSummary.AvgPrice(tradeSummary);
			decimal priceChangeRatio = 0;
			string priceChangeStr=null;
			if (avgPrice!=0 && tradeSummary.previous!=null)
			{
				var previousNonZeroSummary = TradeSummary.FirstNonZeroSummary(tradeSummary.previous);
				if (previousNonZeroSummary != null)
				{
					decimal previousAvgPrice = TradeSummary.AvgPrice(previousNonZeroSummary);
					decimal avgPriceDiff = avgPrice- previousAvgPrice;
					if (avgPriceDiff != 0)
					{
						priceChangeRatio = (avgPriceDiff / previousAvgPrice) * 100M;
						string plusMinus = ((avgPriceDiff >= 0) ? "+" : "-");
						priceChangeStr = $" ({plusMinus}{Decimal.Round(Math.Abs(priceChangeRatio), 2).ToString().PadLeft(5, ' ')}%) ";
					}
					else
					{
						priceChangeStr = "    (same) ";
					}
				}
			}
			if (priceChangeStr == null) priceChangeStr = "          ";

			int numTrades = tradeSummary.numSells + tradeSummary.numBuys;

			//decimal avgSellPrice = ((tradeSummary.numSells == 0) ? 0 : Decimal.Round(tradeSummary.totalSellPrice / (decimal)tradeSummary.numSells, 2));
			//decimal avgBuyPrice = ((tradeSummary.numBuys == 0) ? 0 : Decimal.Round(tradeSummary.totalBuyPrice / (decimal)tradeSummary.numBuys, 2));

			decimal volumeDifference = TradeSummary.TradeDifference(tradeSummary);

			decimal tradeDiffAsPercentOf24Volume=0;
			string tradeDiffAsPercentOf24VolumeStr;
			if (productStats != null)
			{
				tradeDiffAsPercentOf24Volume = (volumeDifference / productStats.Volume) * 100M;
				string plusMinus = ((tradeDiffAsPercentOf24Volume >= 0) ? "+" : "-");
				tradeDiffAsPercentOf24VolumeStr = $" ({plusMinus}{Decimal.Round(Math.Abs(tradeDiffAsPercentOf24Volume), 2).ToString().PadLeft(5, ' ')}%)";
			}
			else
			{
				tradeDiffAsPercentOf24VolumeStr = "          ";
			}

			string extendedStr = string.Empty;
			if (extended)
			{
				decimal priceVolatility;
				if (tradeSummary.highPrice != 0)
				{
					decimal priceDiff = tradeSummary.highPrice - tradeSummary.lowPrice;
					priceVolatility = (priceDiff / tradeSummary.highPrice) * 100;
				}
				else
				{
					priceVolatility = 0;
				}

				// Get the trade difference as a % of the 24 hour volume
				string priceVolCorrelationStr;
				if (priceChangeRatio != 0 && productStats!=null)
				{
					decimal volumePriceCorrelationRatio = Math.Abs(tradeDiffAsPercentOf24Volume - priceChangeRatio);

					decimal volumePriceRatioDiff=(tradeDiffAsPercentOf24Volume - priceChangeRatio);

					//decimal volumePriceRatioDiffRatio = (volumePriceRatioDiff / tradeDiffAsPercentOf24Volume) * 100M;

					decimal pvc3;
					if (tradeDiffAsPercentOf24Volume != 0)
					{
						pvc3 = priceChangeRatio / tradeDiffAsPercentOf24Volume;
					}
					else
					{
						pvc3 = 0;
					}

					priceVolCorrelationStr = $" PVC= -{Decimal.Round(volumePriceCorrelationRatio, 2).ToString().PadLeft(5, ' ')}% " +
						$"PVC3={Decimal.Round(pvc3, 2)}";
				}
				else
				{
					priceVolCorrelationStr = string.Empty;
				}

				extendedStr = $" LP={productInfo.fOutputPrice(tradeSummary.lowPrice)} " +
					$"HP={productInfo.fOutputPrice(tradeSummary.highPrice)} " +
					$"PV={Decimal.Round(priceVolatility,2).ToString().PadLeft(5, ' ')}%"+
					priceVolCorrelationStr;
			}

			return $"L={level.ToString().PadLeft(4, ' ')} " +
				$"VD={productInfo.fOutputVolume(volumeDifference)}{tradeDiffAsPercentOf24VolumeStr} " +
				$"AP={productInfo.fOutputPrice(avgPrice)}" + priceChangeStr+
				$"NTD={productInfo.fOutputNumberOfTrades(TradeSummary.NumTradeDifference(tradeSummary))} " +
				$"V={productInfo.fOutputVolume(tradeSummary.buyTotal + tradeSummary.sellTotal)} " +
				$"VR={volRatio.ToString().PadLeft(5, ' ')} " +extendedStr;
		}

		private static void AddCBPagedProductListIndexes(
			CBPagedTradeList trades, int pageIdx, int tradeIdx, int difference,
			out int newTradeHistoryBeginPageIdx,
			out int newTradeHistoryBeginTradeIdx)
		{
			Debug.Assert(difference == 1 || difference == -1);
			var currentPage = trades[pageIdx];

			var tradeIdxPlusDifference = tradeIdx + difference;

			if (difference > 0)
			{
				if ((tradeIdxPlusDifference - 1) >= currentPage.Count)
				{
					// Overflow to the next page
					newTradeHistoryBeginPageIdx = pageIdx + 1;
					Debug.Assert(newTradeHistoryBeginPageIdx < trades.Count);
					newTradeHistoryBeginTradeIdx = 0;
				}
				else
				{
					newTradeHistoryBeginPageIdx = pageIdx;
					newTradeHistoryBeginTradeIdx = tradeIdxPlusDifference;
				}
			}
			else
			{
				if (tradeIdxPlusDifference < 0)
				{
					// Underflow to the next page
					newTradeHistoryBeginPageIdx = pageIdx - 1;
					Debug.Assert(newTradeHistoryBeginPageIdx >=0);
					newTradeHistoryBeginTradeIdx = trades[newTradeHistoryBeginPageIdx].Count-1;
				}
				else
				{
					newTradeHistoryBeginPageIdx = pageIdx;
					newTradeHistoryBeginTradeIdx = tradeIdxPlusDifference;
				}
			}
		}

		private static bool GetHistoryBeginInfo(TradeHistoryState tradeHistory,
			CBPagedTradeList trades, out DateTimeOffset historyBeginTimeOut,
			out int firstNewTradePageOut,
			out int firstNewTradeIdxOut)
		{
			firstNewTradePageOut = -1;
			firstNewTradeIdxOut = -1;

			// Should have already verified this.
			Debug.Assert(trades.Count>0);
			var lastTradePage = trades[trades.Count - 1];

			// Should have already verified this.
			Debug.Assert(lastTradePage.Count > 0);
			historyBeginTimeOut = lastTradePage[lastTradePage.Count-1].Time;
			historyBeginTimeOut = new DateTime(historyBeginTimeOut.Year, historyBeginTimeOut.Month,
				historyBeginTimeOut.Day, historyBeginTimeOut.Hour, historyBeginTimeOut.Minute, 0).AddMinutes(1);

			// Because we are using a lambda with an out parameter :/
			DateTimeOffset historyBeginTime = historyBeginTimeOut;
			int firstNewTradePage=-1, firstNewTradeIdx=-1;

			bool foundBeginInfo=
				!IterateCBPagedTradeList(trades, false, trades.Count-1,
					(tradeCount) => tradeCount-2 /* We've already checked the last */,
					(iterTrade, pageIdx, tradeIdx) =>
					  {
						  if (iterTrade.Time >= historyBeginTime)
						  {
							  firstNewTradePage = pageIdx;
							  firstNewTradeIdx = tradeIdx;
							  return false;
						  }
						  return true;
					  });

			if (foundBeginInfo)
			{
				firstNewTradePageOut = firstNewTradePage;
				firstNewTradeIdxOut = firstNewTradeIdx;
			}

			return foundBeginInfo;
		}

		public static void Test(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			IEnumerable<CBProductTrade> _trades, SLUpdateTriggerList slUpdateTriggers,
			Action<Exception> HandleExceptions, NewTradesTriggerList newTradesTriggers,
			ProductType productType, Func<ProductStatsDictionary> getProdStats)
		{
			var pagedTrades = new List<IList<CBProductTrade>>();
			pagedTrades.Add(_trades.ToList());

			Test(cbClient,EventOutput, pagedTrades, slUpdateTriggers, HandleExceptions,
				newTradesTriggers, productType, getProdStats);
		}

		public static void Test(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			List<IList<CBProductTrade>> pagedTrades, SLUpdateTriggerList slUpdateTriggers,
			Action<Exception> HandleExceptions, NewTradesTriggerList newTradesTriggers,
			ProductType productType, Func<ProductStatsDictionary> getProdStats)
		{

			var testState = new TradeHistoryState();
			testState.tradeSummaries = new TradeSummaryLevels();

			// Reverse the trades in each page so that it's oldest to newest
			{
				var tempPaged= new List<IList<CBProductTrade>>();
				for (int i = pagedTrades.Count - 1; i >= 0; --i)
				{
					var pageCopy = new List<CBProductTrade>(pagedTrades[i]);
					pageCopy.Reverse();
					tempPaged.Add(pageCopy);
				}

				pagedTrades = tempPaged;
			}

			// Generate some dummy trade ID's and initialise the times if not already
			{
				int tradeId = 10000;
				var defaultTime = DateTime.Now;
				foreach (var tradeList in pagedTrades)
				{
					foreach (var trade in tradeList)
					{
						trade.TradeId = --tradeId;
						if (trade.Time == DateTime.MinValue)
						{
							trade.Time = defaultTime;
							defaultTime = defaultTime.AddMilliseconds(-500);
						}
					}
				}
			}

			var lastTestPage = pagedTrades[pagedTrades.Count - 1];
			var lastTestTrade = lastTestPage[lastTestPage.Count - 1];

			// Add a dummy trade so that the trade history has a time to start from
			var historyStartTrade = new CBProductTrade();
			historyStartTrade.Time = lastTestTrade.Time.AddMinutes(-1);
			lastTestPage.Add(historyStartTrade); // (Oldest)

			NewTradesCallback newTradesCallback=null;
			if (newTradesTriggers != null)
			{
				newTradesCallback = (summaryLevels, recentTrades) =>
				  {
					  Trigger.NewTradesCallback(newTradesTriggers, summaryLevels, getProdStats,
						  HandleExceptions, EventOutput, recentTrades);
				  };
			}

			SummaryLevelUpdateCallback slUpdateCallback = null;
			if (slUpdateTriggers != null)
			{
				slUpdateCallback = (summaryLevels) =>
				{
					Trigger.TradeWatchSummaryLevelUpdateCallback(slUpdateTriggers, summaryLevels,
						getProdStats, HandleExceptions, EventOutput);
				};
			}

			// Add trades per page
			// Important that we do this in reverse order (oldest first), seeing as the pages are
			// currently newest to oldest
			for (int i = pagedTrades.Count-1; i >=0; --i)
			{
				var iterPage = new List<IList<CBProductTrade>>();
				iterPage.Add(pagedTrades[i]);
				
				ConditionalAddTrades(iterPage, testState, null /* Don't output trade summary */,
					null /* Product stats not needed */, slUpdateCallback, newTradesCallback,
					productType);
			}
		}

		private static IList<TradeSummaryState> SummaryLevelHistory(TradeSummaryState history)
		{
			var tradeSummaryFlat = new List<TradeSummaryState>();

			for (var summary = history; summary != null; summary = summary.previous)
			{
				// Skip any summaries where nothing happened.
				if (TradeSummary.TotalVolume(summary) > 0)
				{
					tradeSummaryFlat.Add(summary);
				}
			}

			return tradeSummaryFlat;
		}

		public static IEnumerable<string> DescribeSummaryLevelHistory(
			TradeHistoryState tradeHistoryState, int level, ProductStats productStats,
			ProductType productType, bool extended)
		{
			TradeSummaryLevels slCopy;
			lock (tradeHistoryState.historyLock)
			{
				slCopy = new TradeSummaryLevels(tradeHistoryState.tradeSummaries);
			}

			TradeSummaryState levelState;

			if (!slCopy.TryGetValue(level, out levelState))
			{
				return new string[] { "Invalid level." };
			}

			var rv = new List<string>();
			var tradeSummaryFlat = SummaryLevelHistory(levelState);

			DateTimeOffset lastTs=DateTime.Now.AddYears(-1);

			var productInfo = Products.productInfo[productType];

			for (int i = tradeSummaryFlat.Count - 1; i >= 0; --i)
			{
				var ts = tradeSummaryFlat[i];
				var thisTimestamp = TradeSummary.TradeSummaryEndTime(level, ts);

				Misc.AddNewDayConditionalUpdateLastTs(rv, ref lastTs, thisTimestamp);

				rv.Add($"{Misc.OutputTimestamp(thisTimestamp)} "+
					$"{DescribeTradeSummary(ts, level, productStats, productInfo, extended)}");
			}

			return rv;
		}

		public static async Task<IEnumerable<string>> GetTrades(ProductType product, int numPages,
			Action<Exception> WriteExceptions, CoinbaseProClient cbClient,
			Func<decimal, decimal, bool> includeOnlyOptional)
		{
			var trades=await LoadTrades(numPages, WriteExceptions, cbClient, product);

			// Earliest first.
			var rv = new List<string>();

			var DescribeTrade = FDescribeTradeForGetTrades(rv, includeOnlyOptional);

			IterateCBPagedTradeList(trades, false, trades.Count - 1, (tradeCount) => tradeCount - 1, DescribeTrade);

			return rv;
		}

		private static Func<CBProductTrade, int , int, bool> FDescribeTradeForGetTrades(List<string> outputList,
			Func<decimal, decimal, bool> includeOnlyOptional)
		{
			DateTimeOffset lastTs = DateTime.Now.AddYears(-1);

			return (trade, pageIdx, tradeIdx) =>
			{

				bool include = (includeOnlyOptional == null);
				if (includeOnlyOptional != null)
				{
					decimal sizeWithPlusMinus = ((trade.Side == OrderSide.Buy)?-trade.Size:trade.Size);
					include = includeOnlyOptional(trade.Price, sizeWithPlusMinus);
				}

				if (include)
				{
					Misc.AddNewDayConditionalUpdateLastTs(outputList, ref lastTs, trade.Time);

					string plusMinus;
					// Weirdness with having to swap the sell/buy around
					if (trade.Side == OrderSide.Buy)
					{
						plusMinus = "-";
					}
					else
					{
						plusMinus = "+";
					}

					outputList.Add($"{Misc.OutputTimestamp(trade.Time, true)} " +
						$"{plusMinus}{Decimal.Round(trade.Size, 2).ToString().PadLeft(7, ' ')} " +
						$"{Decimal.Round(trade.Price, 5).ToString().PadLeft(9, ' ')} {trade.TradeId}");
				}
				return true;
			};
		}

		public static IEnumerable<EventOutput> OutputTradeHistory(TradeHistoryState tradeHistory, ProductType product,
			ProductStats productStats)
		{
			TradeSummaryLevels slCopy;
			lock (tradeHistory.historyLock)
			{
				slCopy= new TradeSummaryLevels(tradeHistory.tradeSummaries);
			}

			// Only output level 3
			TradeSummaryState iterTradeSummary;
			if (!slCopy.TryGetValue(TradeSummary.feedOutputSummaryLevel, out iterTradeSummary))
			{
				return null;
			}

			var output = new List<EventOutput>();
			var tradeSummaryFlat = SummaryLevelHistory(iterTradeSummary);
			var productInfo = Products.productInfo[product];
			for (int i = tradeSummaryFlat.Count - 1; i >= 0; --i)
			{
				var summary = tradeSummaryFlat[i];
				output.Add(new EventOutput(DescribeTradeSummary(summary,
					TradeSummary.feedOutputSummaryLevel, productStats, productInfo, true),
					TradeSummary.TradeSummaryEndTime(TradeSummary.feedOutputSummaryLevel, summary)));		
			}

			return output;
		}
#pragma warning disable 1998
		public static async Task<IEnumerable<string>> ShowSummary(ProductType product,
#pragma warning restore 1998
			Action<Exception> WriteExceptions, CoinbaseProClient cbClient, TradeHistoryState tradeHistory,
			DateTimeOffset startTime,DateTimeOffset endTime, ProductStats productStats)
		{
			TradeSummaryState level1Copy;
			lock (tradeHistory.historyLock)
			{
				TradeSummaryState level1;
				if (!tradeHistory.tradeSummaries.TryGetValue(1, out level1))
				{
					return null;
				}

				level1Copy = level1.previous;
			}

			var summary = new TradeSummaryState();
			summary.timeStart = startTime;

			bool foundTheEnd = false;

			for (var iterSummary = level1Copy; iterSummary != null; iterSummary = iterSummary.previous)
			{

				if (iterSummary.timeStart < startTime)
				{
					foundTheEnd = true;
					break;
				}

				DateTimeOffset iterEndTime = TradeSummary.TradeSummaryEndTime(1, iterSummary);

				if (iterEndTime<=endTime)
				{
					TradeSummary.IncrementSummary(summary, iterSummary);
				}
			}

			if (!foundTheEnd) return new string[] {"Data not loaded."};

			var prodInfo = Products.productInfo[product];

			return new string[] { DescribeTradeSummary(summary, 1, productStats, prodInfo, true) };
		}
	}
}
