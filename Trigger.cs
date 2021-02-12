using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradeSummaryLevels = System.Collections.Generic.Dictionary<int /* Summary period in units */, CoinbaseProToolsForm.TradeSummaryState>;
using ProductStatsDictionary = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType, CoinbasePro.Services.Products.Types.ProductStats>;
using ExceptionFileWriter = System.Action<string>;
using ExceptionUIWriter = System.Action<string>;
using EventOutputter = System.Action<System.Collections.Generic.IEnumerable<System.Tuple<string, System.DateTimeOffset?>>>;
using EventOutput = System.Tuple<string, System.DateTimeOffset?>;
using ProductType = CoinbasePro.Shared.Types.ProductType;
using CBProductTrade = CoinbasePro.Services.Products.Models.ProductTrade;
using FindRecentTradesCallback = System.Func<CoinbasePro.Services.Products.Models.ProductTrade,
	CoinbasePro.Services.Products.Models.ProductTrade,
	System.Tuple<bool /* Matches */, bool /* Stop? */, bool /* Move along outer index? */, object /* User data */>>;
using FindRecentTradesCallbackRv = System.Tuple<bool /* Matches */, bool /* Stop? */,
	bool /* Move along outer index? */, object /* User result data */>;
using OrderSide = CoinbasePro.Services.Orders.Types.OrderSide;
using MatchingRecentTrades = System.Tuple<int /* Outer index */, int /* Inner index */, object /* User data */>;
using RapidPriceChangeTriggerUserData = System.Tuple<int, decimal>;
using static CoinbaseProToolsForm.Products;
using static CoinbaseProToolsForm.Library;
using AddSLUpdateTrigger=System.Action<CoinbasePro.Shared.Types.ProductType, CoinbaseProToolsForm.SLUpdateTriggerState>;
using AddNewTradeTrigger = System.Action<CoinbasePro.Shared.Types.ProductType, CoinbaseProToolsForm.NewTradesTriggerState>;
using RemoveNewTradeTrigger = System.Action<CoinbasePro.Shared.Types.ProductType,
	CoinbaseProToolsForm.NewTradesTriggerList,
	int[]>;
using System.IO;
using Newtonsoft.Json;
using Debug = System.Diagnostics.Debug;

namespace CoinbaseProToolsForm
{
	public class SLUpdateTriggerState
	{
		public Func<TradeSummaryLevels, Func<ProductStatsDictionary>, DateTimeOffset,
			IEnumerable<EventOutput>> TriggerFunc;
	}

	public class SLUpdateTriggerList
	{
		public List<SLUpdateTriggerState> triggers = new List<SLUpdateTriggerState>();
	}

	public class NewTradesTriggerState
	{
		public enum eTriggerType
		{
			builtIn = 1,
			priceReach = 2,
		};

		public eTriggerType triggerType;

		public Func<TradeSummaryLevels, Func<ProductStatsDictionary>, List<CBProductTrade>,
			IEnumerable<EventOutput>> TriggerFunc;

		public object PersistWrite;
	}

	public class NewTradesTriggerList
	{
		public List<NewTradesTriggerState> triggers=new List<NewTradesTriggerState>();
		public readonly object theLock=new object();
	}

	public static class Trigger
	{

		public static IList<NewTradesTriggerState> PersistReadNewTradesTriggers(ProductType product)
		{
			var triggers = new List<NewTradesTriggerState>();
			var filename = NewTradeTriggersFilename(product);

			if (File.Exists(filename))
			{
				var fileText = File.ReadAllText(filename);

				object[] fromJson = JsonConvert.DeserializeObject<object[]>(fileText);

				foreach (Newtonsoft.Json.Linq.JObject trigger in fromJson)
				{
					NewTradesTriggerState.eTriggerType triggerType;
					if (Enum.TryParse(trigger.Value<string>("triggerType"), out triggerType))
					{
						var dynamicParts = trigger.Value<Newtonsoft.Json.Linq.JObject>("dynamic");
						switch (triggerType)
						{
							case NewTradesTriggerState.eTriggerType.priceReach:

								triggers.Add(CreateAlertOnPriceTrigger(product, dynamicParts));
								break;
						}
					}
				}
			}

			return triggers;
		}

		private static string NewTradeTriggersFilename(ProductType product)
		{
			return $"newtradetriggers.{product}.txt";
		}

		public static void PersistWriteNewTradesTriggersAssumingAlreadyLocked(ProductType product,NewTradesTriggerList triggers)
		{

			List<object> persistObjectList = new List<object>();

			foreach (var trigger in triggers.triggers)
			{
				if (trigger.PersistWrite != null)
				{
					var persisted = trigger.PersistWrite;
					persistObjectList.Add(new { trigger.triggerType, dynamic=persisted });
				}
			}

			if (persistObjectList != null)
			{
				File.WriteAllText(NewTradeTriggersFilename(product), JsonConvert.SerializeObject(persistObjectList));
			}
		}

		public static void TradeWatchSummaryLevelUpdateCallback(SLUpdateTriggerList triggers,
			TradeSummaryLevels summaryLevels, Func<ProductStatsDictionary> getProductStats,
			Action<Exception> HandleExceptions, EventOutputter Output)
		{
			if (triggers.triggers.Count > 0)
			{

				foreach (var trigger in triggers.triggers)
				{
					//sidtodo once triggered, then what???
					var curDateIsh = TradeSummary.TradeSummaryEndTime(1, summaryLevels[1]);
					var rv = trigger.TriggerFunc(summaryLevels, getProductStats, curDateIsh);

					if (rv != null)
					{
						Output?.Invoke(rv);
						break;
					}
				}
			}
		}

		public static void NewTradesCallback(NewTradesTriggerList triggers,
			TradeSummaryLevels summaryLevels, Func<ProductStatsDictionary> getProductStats,
			Action<Exception> HandleExceptions,
			EventOutputter outputEvent, List<CBProductTrade> recentTrades)
		{
			lock (triggers.theLock)
			{
				if (triggers.triggers.Count > 0)
				{
					foreach (var trigger in triggers.triggers)
					{
						//sidtodo once triggered, then what???
						var rv = trigger.TriggerFunc(summaryLevels, getProductStats, recentTrades);

						if (rv != null)
						{
							outputEvent?.Invoke(rv);
							break;
						}
					}
				}
			}
		}

		//TODO you could pass state between calls to the rules, to prevent expensive operations from being executed
		// multiple times
		public static SLUpdateTriggerState CreateBullRunTradeWatchTrigger(ProductType productType)
		{
			//sidtodo temp hack
			DateTimeOffset? lastTimeTriggered = null;
			var rule = new SLUpdateTriggerState();
			rule.TriggerFunc = (summaryLevels, getProdStats, tsOfEvent) =>
			{
				//var now = DateTime.Now;
				if (lastTimeTriggered.HasValue && (tsOfEvent - lastTimeTriggered.Value).TotalSeconds < (60 * 5)) return null;

				lastTimeTriggered = null;

				// If 8%+ of the 24 hour volume is spent in 15 minutes, trigger
				var prodStats = getProdStats();
				if (prodStats != null)
				{
					var linkStats = prodStats[productType];

					// Calculate the data for the last 15 mins.
					var numUnitsFor15Mins = (15 * 60) / TradeSummary.singleUnitOfTimeSecs;
					var last15MinsSummary = TradeSummary.GetTradeSummary(numUnitsFor15Mins, summaryLevels);

					var eightPercentOfLast24HourVolume = linkStats.Volume * 0.08M;

					var last15MinsTotalVolume = TradeSummary.TotalVolume(last15MinsSummary);

					if (last15MinsTotalVolume >= eightPercentOfLast24HourVolume)
					{
						// Ratio of buy to sell volume is at least 2 to 1.
						var buyRatio = last15MinsSummary.buyTotal / last15MinsTotalVolume;
						if (buyRatio > 0.66M)
						{
							decimal ratio = (last15MinsTotalVolume / linkStats.Volume) * 100M;
							var speech = $"{GetProductSpokenName(productType)} Large buy, {Decimal.Round(ratio, 1)} " +
								$"percent of the 24 hour volume. Potential bull run.";
							Library.AsyncSpeak(speech);
							lastTimeTriggered = tsOfEvent;
							return new EventOutput[] { new EventOutput(speech,
								TradeSummary.TradeSummaryEndTime(1,summaryLevels[1])) };
						}

						//sidtodo temp hack.
						var sellRatio = last15MinsSummary.sellTotal / last15MinsTotalVolume;
						if (sellRatio > 0.66M)
						{
							decimal ratio = (last15MinsTotalVolume / linkStats.Volume) * 100M;
							var speech = $"{GetProductSpokenName(productType)} Large drop, {Decimal.Round(ratio, 1)} " +
								"percent of the 24 hour volume.";
							Library.AsyncSpeak(speech);
							lastTimeTriggered = tsOfEvent;
							return new EventOutput[] { new EventOutput(speech,
								TradeSummary.TradeSummaryEndTime(1, summaryLevels[1])) };
						}
					}
				}

				return null;
			};
			return rule;
		}

		//public static SLUpdateTriggerState CreateLargeVolumeIncreaseOrDecreaseTrigger()
		//{
		//	var rule = new SLUpdateTriggerState();
		//	DateTimeOffset lastRunTimeUp = DateTime.Now.AddYears(-1);
		//	DateTimeOffset lastRunTimeDown = DateTime.Now.AddYears(-1);

		//	rule.TriggerFunc = (summaryLevels, getProdStats, tsOfEvent) =>
		//	{
		//		bool inTimeUp = ((tsOfEvent - lastRunTimeUp).TotalSeconds >= (60 * 4));
		//		bool inTimeDown = ((tsOfEvent - lastRunTimeDown).TotalSeconds >= (60 * 4));
		//		if (inTimeUp || inTimeDown)
		//		{
		//			TradeSummaryState fiveMinSummaryLevel;
		//			if (summaryLevels.TryGetValue(9, out fiveMinSummaryLevel))
		//			{
		//				var tradeDiff = TradeSummary.TradeDifference(fiveMinSummaryLevel);
		//				if (inTimeUp && tradeDiff >= 2000)
		//				{
		//					lastRunTimeUp = tsOfEvent;
		//					var speech = $"Large volume increase in 5 minutes.";
		//					Library.AsyncSpeak(speech);
		//					return new EventOutput[] { new EventOutput(speech,
		//						TradeSummary.TradeSummaryEndTime(1,summaryLevels[1]))};
		//				}

		//				if (inTimeDown && tradeDiff <= -2000)
		//				{
		//					lastRunTimeDown = tsOfEvent;
		//					var speech = $"Large volume decrease in 5 minutes.";
		//					Library.AsyncSpeak(speech);
		//					return new EventOutput[] { new EventOutput(speech,
		//						TradeSummary.TradeSummaryEndTime(1,summaryLevels[1]))};
		//				}
		//			}
		//		}

		//		return null;
		//	};

		//	return rule;
		//}

		//public static SLUpdateTriggerState CreatePriceConsistentlyGoingUpTrigger()
		//{
		//	var rule = new SLUpdateTriggerState();
		//	DateTimeOffset lastRunTime = DateTime.Now.AddYears(-1);

		//	rule.TriggerFunc = (summaryLevels, getProdStats, tsOfEvent) =>
		//	{
		//		if ((tsOfEvent - lastRunTime).TotalMinutes >= 15)
		//		{
		//			TradeSummaryState fifteenMinSummaryLevel;
		//			if (summaryLevels.TryGetValue(27, out fifteenMinSummaryLevel))
		//			{
		//				if (fifteenMinSummaryLevel.previous != null)
		//				{
		//					if (TradeSummary.TradeDifference(fifteenMinSummaryLevel) >= 1000 &&
		//						TradeSummary.TradeDifference(fifteenMinSummaryLevel.previous) >= 1000)
		//					{
		//						lastRunTime = tsOfEvent;
		//						var speech = "Trade volume difference is consistently up.";
		//						Library.AsyncSpeak(speech);
		//						return new EventOutput[] { new EventOutput(speech,
		//							TradeSummary.TradeSummaryEndTime(1,summaryLevels[1]))};
		//					}
		//				}
		//			}
		//		}

		//		return null;
		//	};

		//	return rule;
		//}

		//public static SLUpdateTriggerState CreatePriceConsistentlyGoingDownTrigger()
		//{
		//	var rule = new SLUpdateTriggerState();
		//	DateTimeOffset lastRunTime = DateTime.Now.AddYears(-1);

		//	rule.TriggerFunc = (summaryLevels, getProdStats, tsOfEvent) =>
		//	{
		//		if ((tsOfEvent - lastRunTime).TotalMinutes >= 15)
		//		{
		//			TradeSummaryState fifteenMinSummaryLevel;
		//			if (summaryLevels.TryGetValue(27, out fifteenMinSummaryLevel))
		//			{
		//				if (fifteenMinSummaryLevel.previous != null)
		//				{
		//					if (TradeSummary.TradeDifference(fifteenMinSummaryLevel) <= -1000 &&
		//						TradeSummary.TradeDifference(fifteenMinSummaryLevel.previous) <= -1000)
		//					{
		//						lastRunTime = tsOfEvent;
		//						var speech = "Trade volume difference is consistently down.";
		//						Library.AsyncSpeak(speech);
		//						return new EventOutput[] { new EventOutput(speech,
		//							TradeSummary.TradeSummaryEndTime(1, summaryLevels[1])) };
		//					}
		//				}
		//			}
		//		}

		//		return null;
		//	};

		//	return rule;
		//}

		//public static SLUpdateTriggerState CreateHighPriceReachValueTrigger(decimal price, string textToSay,
		//	ProductType productType)
		//{
		//	var rule = new SLUpdateTriggerState();
		//	decimal previousPrice = 0;
		//	DateTimeOffset? lastRunTime = null;

		//	rule.TriggerFunc = (summaryLevels, getProdStats, tsOfEvent, innerProdType) =>
		//	{

		//		if (innerProdType != productType) return null;

		//		bool rv = false;

		//		var summaryLevel1 = TradeSummary.FirstNonZeroSummary(summaryLevels[1]);

		//		if (previousPrice != 0)
		//		{
		//			rv = (previousPrice < price && summaryLevel1.highPrice >= price);
		//		}

		//		previousPrice = summaryLevel1.highPrice;

		//		if (rv)
		//		{
		//			const int twelveHoursInSeconds = (60 * 60 * 12);

		//			rv = (!lastRunTime.HasValue || (tsOfEvent - lastRunTime.Value).TotalSeconds >= twelveHoursInSeconds);

		//			if (rv)
		//			{
		//				Library.AsyncSpeak(textToSay);

		//				lastRunTime = tsOfEvent;

		//				return new EventOutput[] { new EventOutput(textToSay,
		//					TradeSummary.TradeSummaryEndTime(1,summaryLevels[1]))};
		//			}
		//		}

		//		return null;
		//	};

		//	return rule;
		//}

		//public static SLUpdateTriggerState CreateLowPriceReachValueTrigger(decimal price, string textToSay,
		//	ProductType productType)
		//{
		//	var rule = new SLUpdateTriggerState();
		//	decimal previousPrice = 0;
		//	DateTimeOffset? lastRunTime = null;

		//	rule.TriggerFunc = (summaryLevels, getProdStats, tsOfEvent, innerProdType) =>
		//	{

		//		if (innerProdType != productType) return null;

		//		bool rv = false;

		//		var summaryLevel1 = TradeSummary.FirstNonZeroSummary(summaryLevels[1]);
		//		if (summaryLevel1 != null)
		//		{
		//			if (previousPrice != 0)
		//			{
		//				rv = (previousPrice > price && summaryLevel1.lowPrice <= price);
		//			}

		//			previousPrice = summaryLevel1.lowPrice;

		//			if (rv)
		//			{
		//				const int twelveHoursInSeconds = (60 * 60 * 12);
		//				//var now = DateTime.Now;

		//				rv = (!lastRunTime.HasValue || (tsOfEvent - lastRunTime.Value).TotalSeconds >= twelveHoursInSeconds);

		//				if (rv)
		//				{
		//					Library.AsyncSpeak(textToSay);

		//					lastRunTime = tsOfEvent;

		//					return new EventOutput[] { new EventOutput(textToSay,
		//						TradeSummary.TradeSummaryEndTime(1,summaryLevels[1]))};
		//				}
		//			}
		//		}

		//		return null;
		//	};

		//	return rule;
		//}

		public static SLUpdateTriggerState CreateSteadyPriceChangeTrigger(int durationSecs,
			decimal percentageToChange, Func<int, decimal, string> getAlertMessage,
			Func<bool> fIsEnabled, Func<bool> fIsSpeechEnabled)
		{
			DateTimeOffset lastTimeSpoken = DateTime.Now.AddDays(-1);
			var rule = new SLUpdateTriggerState();

			rule.TriggerFunc = (summaryLevels, getProdStats, tsOfEvent) =>
			{
				if (!fIsEnabled()) return null;

				var now = DateTime.Now;
				if ((now - lastTimeSpoken).TotalSeconds <= durationSecs)
				{
					return null;
				}

				// 10 minutes in units.
				int numLevel1Units = (int)(durationSecs / TradeSummary.singleUnitOfTimeSecs);

				var tenMinSummary = TradeSummary.GetTradeSummary(numLevel1Units, summaryLevels);

				if (TradeSummary.NumTrades(tenMinSummary) == 0) return null;

				decimal priceDiffPercentage;
				if (tenMinSummary.lowPriceTs < tenMinSummary.highPriceTs)
				{
					// Price gone up.
					decimal priceDiff = tenMinSummary.highPrice - tenMinSummary.lowPrice;
					priceDiffPercentage = (priceDiff / tenMinSummary.lowPrice) * 100;
				}
				else
				{
					decimal priceDiff = tenMinSummary.lowPrice - tenMinSummary.highPrice;
					priceDiffPercentage = (priceDiff / tenMinSummary.highPrice) * 100;
				}

				bool matchesTrigger = false;

				if (percentageToChange > 0 && priceDiffPercentage >= percentageToChange)
				{
					matchesTrigger = true;
				}
				else if (percentageToChange < 0 && priceDiffPercentage <= percentageToChange)
				{
					matchesTrigger = true;
				}

				if (matchesTrigger)
				{
					int timeDiffMs = Math.Abs((int)(tenMinSummary.lowPriceTs - tenMinSummary.highPriceTs).TotalMilliseconds);

					var text = getAlertMessage(timeDiffMs, priceDiffPercentage);

					if (fIsSpeechEnabled())
					{
						Library.AsyncSpeak(text);
					}

					// The event happened at the later time.
					if (tenMinSummary.lowPriceTs < tenMinSummary.highPriceTs)
					{
						lastTimeSpoken = tenMinSummary.highPriceTs;
					}
					else
					{
						lastTimeSpoken = tenMinSummary.lowPriceTs;
					}

					return new EventOutput[] { new EventOutput(text, lastTimeSpoken) };
				}

				return null;
			};

			return rule;
		}

		//sidtodo do a rapid mode.
		//sidtodo move this to new trades???
		public static SLUpdateTriggerState CreateSpeakPriceTrigger(State state,
			Func<ProductType> getProductType, ProductType productType)
		{
			DateTimeOffset? lastTimeSpoke = null;
			var rule = new SLUpdateTriggerState();
			decimal previousPrice = 0;

			rule.TriggerFunc = (summaryLevels, getProdStats, tsOfEvent) =>
			{

				if (state.options.SpeakPrice == false) return null;

				if (productType != getProductType()) return null;

				var summaryLevel1 = TradeSummary.FirstNonZeroSummary(summaryLevels[1]);
				decimal thisPrice = TradeSummary.AvgPrice(summaryLevel1);

				if (previousPrice == 0 && thisPrice == 0) return null;

				// Repeat every 5 minutes unless there is a >1% change
				const int fiveMinutes = (60 * 5);
				bool isFirstTime = !lastTimeSpoke.HasValue;
				bool fiveMinsHasPassed = (lastTimeSpoke.HasValue && (tsOfEvent - lastTimeSpoke.Value).TotalSeconds >= fiveMinutes);
				bool speak = (fiveMinsHasPassed || (isFirstTime && thisPrice != 0));

				string percentageChangeStr = string.Empty;
				if (thisPrice != 0 && previousPrice != 0 && thisPrice != previousPrice)
				{
					decimal diff = previousPrice - thisPrice;
					decimal ratioDiff = (Math.Abs(diff) / previousPrice) * 100M;

					bool is1PercentChange = (ratioDiff >= 1M);

					if (!speak)
					{
						speak = is1PercentChange;
					}

					if (is1PercentChange)
					{
						var ratioDiffRounded = Decimal.Round(ratioDiff, 1);
						percentageChangeStr = $" {ratioDiffRounded} percent {((thisPrice > previousPrice) ? "increase" : "decrease")}.";
					}
				}

				if (speak)
				{

					lastTimeSpoke = tsOfEvent;

					if (thisPrice == 0) thisPrice = previousPrice;
					else previousPrice = thisPrice;

					var prodInfo = Products.productInfo[productType];

					var priceText = $"Price is £{Decimal.Round(thisPrice, 2)}.";

					var text = $"{priceText}{percentageChangeStr} {priceText}{percentageChangeStr}";

					Library.AsyncSpeak(text);

					return null;
				}

				return null;
			};

			return rule;
		}

		//sidtodo check out of the ordinary volume
		public static NewTradesTriggerState CreateRapidBuyOrSell(int durationMs,
			decimal volumePercentageToChange, Func<int, decimal, string> getAlertMessage,
			Func<bool> fIsEnabled, Func<bool> fIsSpeechEnabled, ProductType productType)
		{
			int lastTradeId = 0;

			var rule = new NewTradesTriggerState();

			OrderSide orderSideOfTrigger = (volumePercentageToChange > 0) ? OrderSide.Sell : OrderSide.Buy;

			//sidtodo here: should this be the date of teh outerindex or innerindex?
			DateTime lastTimeSpoken = DateTime.Now.AddDays(-1);

			rule.triggerType = NewTradesTriggerState.eTriggerType.builtIn;

			rule.TriggerFunc = (summaryLevels, getProdStats, recentTrades) =>
			{
				if (fIsEnabled() != true) return null;

				// Once per duration.
				//sidtodo is this right? don't we want to do it per instance by using the last trade ID????
				var now = DateTime.Now;
				if ((int)(now - lastTimeSpoken).TotalMilliseconds < durationMs)
				{
					return null;
				}

				var thisProductProdStats = getProdStats()[productType];

				decimal accumulatedVolume = 0;
				CBProductTrade currentOuterTrade = null;
				decimal largedAccumulatedVolume = 0;
				CBProductTrade currentInnerTrade = null;
				int largestVolumeMs=0;

				CBProductTrade matchingOuterTrade = null;
				CBProductTrade matchingInnerTrade = null;

				Action updateLargeestAccumulatedVolume = () =>
				{
					if (currentOuterTrade != null)
					{
						if (accumulatedVolume > largedAccumulatedVolume)
						{
							matchingOuterTrade = currentOuterTrade;
							matchingInnerTrade = currentInnerTrade;

							largedAccumulatedVolume = accumulatedVolume;
							largestVolumeMs = (int)(currentOuterTrade.Time - currentInnerTrade.Time).TotalMilliseconds;
						}
					}
				};

				FindRecentTradesCallback callback = (outerTrade, innerTrade) =>
				{

					// Within the time frame??
					int timeDiffMs = (int)(outerTrade.Time - innerTrade.Time).TotalMilliseconds;
					if (timeDiffMs >= durationMs)
					{
						return new FindRecentTradesCallbackRv(false /* No match */, false /* Don't stop */,
							true /* Move outer index along - outside time range */, null);
					}

					if (currentOuterTrade ==null || currentOuterTrade.TradeId != outerTrade.TradeId)
					{

						updateLargeestAccumulatedVolume();

						currentOuterTrade = outerTrade;

						// Reset the volume
						accumulatedVolume = outerTrade.Size;
					}

					currentInnerTrade = innerTrade;

					accumulatedVolume += innerTrade.Size;

					return new FindRecentTradesCallbackRv(false, false /* Don't stop */,
						false /* Don't increment outer index */, null);

				};

				FindRecentTrades(recentTrades, orderSideOfTrigger, callback, ref lastTradeId);

				updateLargeestAccumulatedVolume();

				decimal largestAccumulatedVolumePercentageOf24Hour = (largedAccumulatedVolume / thisProductProdStats.Volume) * 100;

				//sidtodo volatility factor is passed in.

				if (largestAccumulatedVolumePercentageOf24Hour >= Math.Abs(volumePercentageToChange))
				{
					if (volumePercentageToChange < 0)
					{
						largestAccumulatedVolumePercentageOf24Hour *= -1;
					}

					var text = getAlertMessage(largestVolumeMs, largestAccumulatedVolumePercentageOf24Hour);

					if (fIsSpeechEnabled())
					{
						Library.AsyncSpeak(text);
					}

					lastTimeSpoken = now; //sidtodo here: should this be the time of the inner trade or the outer trade????

					return new EventOutput[] { new EventOutput(text, matchingOuterTrade.Time) };
				}

				return null;

			};

			return rule;
		}

		public static NewTradesTriggerState CreateRapidPriceChangeTrigger(int durationMs,
			decimal percentageToChange, Func<int, decimal, string> getAlertMessage,
			Func<bool> fIsEnabled, Func<bool> fIsSpeechEnabled)
		{
			int lastTradeId = 0;

			var rule = new NewTradesTriggerState();

			//OrderSide orderSideOfTrigger = (percentageToChange > 0) ? OrderSide.Sell : OrderSide.Buy;

			DateTime lastTimeSpoken = DateTime.Now.AddDays(-1);

			rule.triggerType = NewTradesTriggerState.eTriggerType.builtIn;

			rule.TriggerFunc = (summaryLevels, getProdStats, recentTrades) =>
			{

				if (fIsEnabled() != true) return null;

				//sidtodo don't think this is needed - don't we want to do it per instance by using the last trade ID??
				// Once per duration.
				var now = DateTime.Now;
				if ((int)(now - lastTimeSpoken).TotalMilliseconds < durationMs)
				{
					return null;
				}

				FindRecentTradesCallback callback = (outerTrade, innerTrade) =>
				{
					// Within the time frame??
					int timeDiffMs = (int)(outerTrade.Time - innerTrade.Time).TotalMilliseconds;
					if (timeDiffMs >= durationMs)
					{
						return new FindRecentTradesCallbackRv(false /* No match */, false /* Don't stop */,
							true /* Move outer index along - outside time range */, null);
					}

					// Change in price???
					decimal priceDiff = outerTrade.Price - innerTrade.Price;
					decimal priceDiffRatio = (priceDiff / innerTrade.Price) * 100;
					bool isMatch = false;
					object userData = null;
					if ((percentageToChange > 0 && priceDiffRatio >= percentageToChange) ||
						(percentageToChange < 0 && priceDiffRatio <= percentageToChange))
					{
						isMatch = true;
						userData = new RapidPriceChangeTriggerUserData(timeDiffMs, priceDiffRatio);
					}

					return new FindRecentTradesCallbackRv(isMatch, false /* Don't stop */,
						false /* Don't increment outer index */, userData);

				};

				int orgLastTradeId = lastTradeId;

				MatchingRecentTrades matchingTrades = FindRecentTrades(recentTrades, null, callback, ref lastTradeId);

				if (matchingTrades != null)
				{

					var userData = (RapidPriceChangeTriggerUserData)matchingTrades.Item3;

					var text = getAlertMessage(userData.Item1, userData.Item2);

					if (fIsSpeechEnabled())
					{
						Library.AsyncSpeak(text);
					}

					lastTimeSpoken = now; //sidtodo here: should this be the time of the inner trade or the outer trade????

					return new EventOutput[] { new EventOutput(text, recentTrades[matchingTrades.Item1].Time) };
				}

				return null;
			};

			return rule;
		}

		public static MatchingRecentTrades FindRecentTrades(List<CBProductTrade> recentTrades,
			OrderSide? orderSide, FindRecentTradesCallback callback, ref int lastTradeId)
		{

			MatchingRecentTrades rv = null;
			bool stop = false;

			// Iterate descending (newer trades to old trades)
			for (int i = recentTrades.Count - 1; i >= 1 /* Not zero! */ && !stop; --i)
			{

				var outerTrade = recentTrades[i];

				if (outerTrade.TradeId == lastTradeId)
				{
					// Stop iterating. We reached the last trade ID.
					break;
				}

				// Correct type??
				if (orderSide==null || outerTrade.Side == orderSide)
				{

					for (int ii = i - 1; ii >= 0; --ii)
					{

						var innerTrade = recentTrades[ii];

						if (orderSide == null || innerTrade.Side == outerTrade.Side)
						{
							FindRecentTradesCallbackRv callbackRv = callback(outerTrade, innerTrade);

							if (callbackRv.Item1 /* Match */)
							{
								rv = new MatchingRecentTrades(i, ii, callbackRv.Item4);
								stop = true;
								break;
							}

							if (callbackRv.Item2 /* Stop */)
							{
								stop = true;
								break;
							}

							if (callbackRv.Item3 /* Move outer index */)
							{
								// Move the outer index along
								break;
							}
						}
					}
				}
			}

			int previousLastTradeId = lastTradeId;

			if (rv != null)
			{
				// The last trade is the outer trade of the match
				lastTradeId = recentTrades[rv.Item1].TradeId;
			}
			else
			{
				// Set the last recent trade ID
				// The last trade will be the first trade of the matching type.
				for (int i = recentTrades.Count - 1; i >= 0; --i)
				{
					var iterTrade = recentTrades[i];
					if (orderSide == null || iterTrade.Side == orderSide)
					{
						lastTradeId = iterTrade.TradeId;
						break;
					}
				}

			}

			Debug.Assert(lastTradeId >= previousLastTradeId);
			if (lastTradeId < previousLastTradeId)
			{
				throw new Exception("Last trade is earlier than the previous last trade. Bug.");
			}

			return rv;
		}

		public static IEnumerable<string> CmdLine(string[] cmdSplit,
			Func<ProductType> getProduct, AddSLUpdateTrigger addSQLUpdateTrigger,
			AddNewTradeTrigger addNewTradeTrigger, RemoveNewTradeTrigger removeNewTradeTrigger,
			Dictionary<ProductType,NewTradesTriggerList> newTradesTriggers)
		{
			if (StringCompareNoCase(cmdSplit[1], "ADDPRICE"))
			{
				return AddPriceTrigger(cmdSplit, getProduct, addNewTradeTrigger, newTradesTriggers);
			}
			else if (StringCompareNoCase(cmdSplit[1], "REMOVEPRICE"))
			{
				return RemovePriceTrigger(cmdSplit, getProduct, removeNewTradeTrigger, newTradesTriggers);
			}
			else if (StringCompareNoCase(cmdSplit[1], "LIST"))
			{
				return ListCustomTriggers(cmdSplit, getProduct, newTradesTriggers);
			}
			else if (StringCompareNoCase(cmdSplit[1], "CLEARPRICE"))
			{
				return RemovePriceTriggers(cmdSplit, getProduct, removeNewTradeTrigger, newTradesTriggers);
			}

			return new string[] { $"Invalid command {cmdSplit[1]}" };
		}

		private static IEnumerable<string> ListCustomTriggers(string[] cmdSplit,
			 Func<ProductType> getProduct, Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers)
		{
			bool all = false;
			if (cmdSplit.Length == 3)
			{
				if (StringCompareNoCase(cmdSplit[2], "ALL"))
				{
					all = true;
				}
				else
				{
					return new string[] { $"Unknown option {cmdSplit[2]}" };
				}
			}

			var output = new List<string>();

			Action<ProductType, NewTradesTriggerList> outputTriggerList = (product,triggers) =>
			{
				lock (triggers.theLock)
				{
					for (var i = 0; i < triggers.triggers.Count; ++i)
					{
						var trigger = triggers.triggers[i];
						switch (trigger.triggerType)
						{
							case NewTradesTriggerState.eTriggerType.priceReach:
								dynamic props = trigger.PersistWrite;
								string optionalCoinName;
								if (all)
								{
									optionalCoinName = $"{Products.productInfo[product].name} ";
								}
								else
								{
									optionalCoinName = string.Empty;
								}
								output.Add($"{optionalCoinName}{((all) ? "p" : "P")}rice reach trigger on {props.price}");
								break;
						}
					}
				}
			};

			if (all)
			{
				foreach (var kvp in newTradesTriggers)
				{
					outputTriggerList(kvp.Key, kvp.Value);
				}
			}
			else
			{
				var product = getProduct();
				var kvp = newTradesTriggers[product];
				outputTriggerList(product,kvp);
			}
			
			return output;
		}

		private static IEnumerable<string> RemovePriceTriggers(string[] cmdSplit, Func<ProductType> getProduct,
			RemoveNewTradeTrigger removeNewTradeTrigger,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers)
		{
			bool all = false;
			if (cmdSplit.Length == 3)
			{
				if (StringCompareNoCase(cmdSplit[2], "ALL"))
				{
					all = true;
				}
				else
				{
					return new string[] { $"Unknown option {cmdSplit[2]}" };
				}
			}

			Action<ProductType, NewTradesTriggerList> removePriceTriggers = (product, triggers) =>
			{
				lock (triggers.theLock)
				{
					var indexList = new List<int>();
					for (var i = 0; i < triggers.triggers.Count; ++i)
					{
						var trigger = triggers.triggers[i];
						switch (trigger.triggerType)
						{
							case NewTradesTriggerState.eTriggerType.priceReach:
								indexList.Add(i);
								break;
						}
					}

					removeNewTradeTrigger(product, triggers, indexList.ToArray());
				}
			};

			if (all)
			{
				foreach (var kvp in newTradesTriggers)
				{
					removePriceTriggers(kvp.Key, kvp.Value);
				}
			}
			else
			{
				var product = getProduct();
				var kvp = newTradesTriggers[product];
				removePriceTriggers(product, kvp);
			}

			return null;
		}

		private static IEnumerable<string> RemovePriceTrigger(string[] cmdSplit, Func<ProductType> getProduct,
			RemoveNewTradeTrigger removeNewTradeTrigger,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers)
		{
			if (cmdSplit.Length == 2) return new string[] { "Price not specified." };

			decimal price;
			if (!decimal.TryParse(cmdSplit[2], out price))
			{
				return new string[] { $"Invalid price {cmdSplit[2]}" };
			}

			var product = getProduct();
			var triggers = newTradesTriggers[product];

			lock (triggers.theLock)
			{
				for(var i=0;i< triggers.triggers.Count;++i)
				{
					var trigger = triggers.triggers[i];
					switch (trigger.triggerType)
					{
						case NewTradesTriggerState.eTriggerType.priceReach:
							dynamic props = trigger.PersistWrite;
							if (props.price == price)
							{
								removeNewTradeTrigger(product, triggers, new int[] { i });
								return new string[] { $"Price removed." };
							}
							break;
					}
				}
			}

			return new string[] { $"Trigger for price {price} does not exist." };
		}

		private static IEnumerable<string> AddPriceTrigger(string[] cmdSplit,Func<ProductType> getProduct,
			AddNewTradeTrigger addTrigger, Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers)
		{
			if (cmdSplit.Length == 2) return new string[] { "Price not specified."};

			decimal price;
			if (!decimal.TryParse(cmdSplit[2], out price))
			{
				return new string[] {$"Invalid price {cmdSplit[2]}"};
			}

			var product = getProduct();

			var triggers = newTradesTriggers[product];

			lock (triggers.theLock)
			{
				addTrigger(product, CreateAlertOnPriceTrigger(price, product));
			}

			return null;
		}

		private static NewTradesTriggerState CreateAlertOnPriceTrigger(ProductType product,
			Newtonsoft.Json.Linq.JObject json)
		{

			decimal price;
			if (!decimal.TryParse(json.Value<string>("price"), out price))
			{
				throw new Exception("Invalid price in trigger.");
			}

			return CreateAlertOnPriceTrigger(price, product);
		}

		public static NewTradesTriggerState CreateAlertOnPriceTrigger(decimal triggerPrice,
			ProductType product)
		{
			var rule = new NewTradesTriggerState();
			bool goingUp=false;
			DateTimeOffset lastSpeakTime = DateTime.Now.AddYears(-1);
			var productInfo = Products.productInfo[product];
			decimal previousPrice = 0;
			DateTimeOffset lastRunTime = DateTime.Now.AddYears(-1);

			rule.triggerType = NewTradesTriggerState.eTriggerType.priceReach;

			rule.TriggerFunc = (summaryLevels, getProdStats, trades) =>
			{

				if (trades.Count == 0) return null;

				if (previousPrice ==0 ) {
					previousPrice = trades[trades.Count-1].Price;
					goingUp = (previousPrice < triggerPrice);
				}

				EventOutput[] rv=null;

				for (int i=trades.Count-1;i>=0;--i)
				{
					var trade = trades[i];
					if (trade.Time < lastRunTime)
					{
						// The recent trade list contains data from teh last 5 minutes.
						break;
					}

					bool trigger = false;
					if (goingUp)
					{
						trigger = (trade.Price >= triggerPrice && previousPrice<triggerPrice);
					}
					else
					{
						trigger = (trade.Price <= triggerPrice && previousPrice > triggerPrice);
					}

					if (trigger)
					{
						previousPrice = trade.Price;

						const int fiveMinsInSeconds = (60 * 5);
						trigger = ((trade.Time - lastSpeakTime).TotalSeconds >= fiveMinsInSeconds);
						if (trigger)
						{
							var textToSay = $"{productInfo.spokenName} has reached {productInfo.fSpeakPrice(triggerPrice)}.";
							AsyncSpeak(textToSay);
							lastSpeakTime = trade.Time;

							rv=new EventOutput[] { new EventOutput(textToSay,null)};
						}
					}
				}

				lastRunTime = trades[trades.Count - 1].Time;

				return rv;
			};

			rule.PersistWrite = new { price=triggerPrice };

			return rule;
		}
	}
}