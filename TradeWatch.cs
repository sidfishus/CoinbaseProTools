using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoinbaseProToolsForm.Library;
using CoinbasePro;
using Types = CoinbasePro.Shared.Types;
using CoinbasePro.WebSocket.Models.Response;
using TradeSummaryLevels=System.Collections.Generic.Dictionary<int /* Summary period in units */, CoinbaseProToolsForm.TradeSummaryState>;
using TradeList = System.Collections.Generic.List<CoinbasePro.WebSocket.Models.Response.Ticker>;
using CoinbasePro.Services.Orders.Types;
using ProductStatsDictionary = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType, CoinbasePro.Services.Products.Types.ProductStats>;
using ExceptionFileWriter = System.Action<string>;
using EventOutputter = System.Action<System.Collections.Generic.IEnumerable<System.Tuple<string, System.DateTimeOffset?>>>;
using EventOutput = System.Tuple<string, System.DateTimeOffset?>;
using ExceptionUIWriter = System.Action<string>;
using SummaryLevelUpdateCallback = System.Action<System.Collections.Generic.Dictionary<int /* Summary period in units */, CoinbaseProToolsForm.TradeSummaryState>>;
using CBProductTrade = CoinbasePro.Services.Products.Models.ProductTrade;
using ProductStats = CoinbasePro.Services.Products.Types.ProductStats;
using ProductType = CoinbasePro.Shared.Types.ProductType;
using ChannelType = CoinbasePro.WebSocket.Types.ChannelType;
using FAmountInTopList = System.Func<decimal /* Top bid */, decimal /* Top ask */, decimal /* Num coins */>;
using PriceList = System.Collections.Generic.SortedDictionary<decimal, decimal>;
using Debug = System.Diagnostics.Debug;
using static CoinbaseProToolsForm.Misc;
using System.Threading;
using Accounts = CoinbasePro.Services.Accounts;
using GetOrderRes = System.Tuple<CoinbasePro.Services.Orders.Models.Responses.OrderResponse, bool>;

namespace CoinbaseProToolsForm
{
	class LimitTaskSynchronisation
	{
		public TimedLock theLock=new TimedLock();
		public string currentOrderId=null;
		public decimal currentPrice=0;
		public decimal currentAmountToTrade=0;
	}

	public static class TradeWatch
	{

		static readonly string s_ShowSummaryCmdLine= "tw showsummary [start date] <start time> [end date] <end time>";
		static readonly string s_BuyTheDipCmdLine =
			"BUYTHEDIP <funds/all/half/third/quarter> <max price> [optional rebuy price]";

		public static async Task<IEnumerable<string>> CmdLine(CoinbaseProClient cbClient,
			string[] cmdSplit, EventOutputter EventOutput,
			Func<ProductStatsDictionary> getProdStats,
			Action<Exception> HandleExceptions, TradeHistoryState tradeHistoryState,
			Dictionary<ProductType,NewTradesTriggerList> newTradesTriggers,
			Func<ProductType> getActiveProduct, Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			LockedByRef<InProgressCommand> inProgressCmd, WebSocketState webSocketState,
			Action<bool> fEnableNetworkTraffic)
		{

			if (StringCompareNoCase(cmdSplit[0], "BUYL"))
			{
				return await BuyLimit(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic);
			}
			else if (StringCompareNoCase(cmdSplit[0], "SELLL"))
			{
				return await SellLimit(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic);
			}

			else if (StringCompareNoCase(cmdSplit[0], "BUYM"))
			{
				return await BuyMarket(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic);
			}
			else if (StringCompareNoCase(cmdSplit[0], "SELLM"))
			{
				return await SellMarket(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic);
			}

			else if (StringCompareNoCase(cmdSplit[0], "BUYMATPRICE"))
			{
				return await BuyMarketAtPrice(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic);
			}
			else if (StringCompareNoCase(cmdSplit[0], "SELLMATPRICE"))
			{
				return await SellMarketAtPrice(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic);
			}

			else if (StringCompareNoCase(cmdSplit[0], "BUYTHEDIP"))
			{
				return await BuyTheDip(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic);
			}

			else if (StringCompareNoCase(cmdSplit[1], "TEST1"))
			{
				Test1(cbClient, EventOutput,
					 HandleExceptions, slUpdateTriggers, newTradesTriggers,
					 getProdStats);
			}
			else if (StringCompareNoCase(cmdSplit[1], "TEST2"))
			{
				Test2(cbClient, EventOutput, getProdStats,
					slUpdateTriggers, HandleExceptions, newTradesTriggers);
			}
			else if (StringCompareNoCase(cmdSplit[1], "TEST3"))
			{
				Test3(cbClient, EventOutput,
					 HandleExceptions, slUpdateTriggers, newTradesTriggers,
					 getProdStats);
			}
			else if (StringCompareNoCase(cmdSplit[1], "TEST4"))
			{
				Test4(cbClient, EventOutput,
					 HandleExceptions, slUpdateTriggers, newTradesTriggers,
					 getProdStats);
			}
			else if (StringCompareNoCase(cmdSplit[1], "TEST5"))
			{
				Test5(cbClient, EventOutput,
					 HandleExceptions, slUpdateTriggers, newTradesTriggers,
					 getProdStats);
			}
			else if (StringCompareNoCase(cmdSplit[1], "TEST6"))
			{
				Test6(cbClient, EventOutput,
					 HandleExceptions, slUpdateTriggers, newTradesTriggers,
					 getProdStats);
			}
			else if (StringCompareNoCase(cmdSplit[1], "TEST7"))
			{
				Test7(cbClient, EventOutput,
					 HandleExceptions, slUpdateTriggers, newTradesTriggers,
					 getProdStats);
			}
			else if (StringCompareNoCase(cmdSplit[1], "TEST8"))
			{
				// Test the steady price increase/decrease trigger
				Test8(cbClient, EventOutput,
					 HandleExceptions, slUpdateTriggers, newTradesTriggers,
					 getProdStats);
			}
			else if (StringCompareNoCase(cmdSplit[1], "TEST9"))
			{
				// Test the large sell trigger
				Test9(cbClient, EventOutput,
					 HandleExceptions, slUpdateTriggers, newTradesTriggers,
					 getProdStats);
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWLEVEL"))
			{
				return ShowLevel(cmdSplit, tradeHistoryState, getProdStats, false, getActiveProduct());
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWLEVELEX"))
			{
				return ShowLevel(cmdSplit, tradeHistoryState, getProdStats, true, getActiveProduct());
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWTRADES"))
			{
				return await ShowTrades(cmdSplit, tradeHistoryState, HandleExceptions,
					cbClient, null, getActiveProduct);
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWSELLS"))
			{
				return await ShowTrades(cmdSplit, tradeHistoryState, HandleExceptions,
					cbClient, (price, size) => size < 0, getActiveProduct);
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWBUYS"))
			{
				return await ShowTrades(cmdSplit, tradeHistoryState, HandleExceptions,
					cbClient, (price, size) => size > 0, getActiveProduct);
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWSUMMARY"))
			{
				return await ShowSummary(cmdSplit, tradeHistoryState, HandleExceptions, getProdStats, getActiveProduct,
					cbClient);
			}

			return null;
		}

		private static DateTime? ReadTimestamp(bool includeDate, string[] cmdSplit, int idx)
		{
			string inputTimeFormat;
			string parseTimeFormat;

			if (includeDate)
			{
				//sidtodo: this is a library method.
				parseTimeFormat = "dd-MM HH:mm";

				string dateStr = cmdSplit[idx];
				string timeStr = cmdSplit[idx + 1];

				inputTimeFormat = $"{dateStr} {timeStr}";
			}
			else
			{
				parseTimeFormat = "HH:mm";
				string timeStr = cmdSplit[idx];

				inputTimeFormat = timeStr;
			}

			DateTime theTime;
			try
			{
				var provider = System.Globalization.CultureInfo.InvariantCulture;
				theTime = DateTime.ParseExact(inputTimeFormat, parseTimeFormat, provider);
				return theTime;
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Func<OrderBookLevel2, decimal, decimal> LimitTaskGetOurPrice(
			int numUnderCutUnits, ProductInfo prodInfo,
			Func<OrderBookLevel2, decimal> fBestPrice, OrderSide orderSide,
			decimal minOrMaxPrice, decimal minOrMaxPricePercentage)
		{
			Debug.Assert(minOrMaxPrice == 0 || minOrMaxPricePercentage == 0);

			DateTime fullChangeTs = DateTime.Now.AddHours(-1);

			decimal underCutUnit = prodInfo.smallestPriceDivision;

			Func<decimal, decimal, decimal> fPriceAlteration;
			Func<decimal, decimal, bool> fIsABetterPrice;
			if (orderSide == OrderSide.Buy)
			{
				fPriceAlteration = (lhs, rhs) => lhs + rhs;
				fIsABetterPrice = (lhs, rhs) => (lhs > rhs);
			}
			else
			{
				fPriceAlteration = (lhs, rhs) => lhs - rhs;
				fIsABetterPrice = (lhs, rhs) => (lhs < rhs);
			}

			Func<decimal, decimal> fMinOrMaxPrice=null;
			if (minOrMaxPricePercentage != 0)
			{
				fMinOrMaxPrice = (initialPrice) =>
					Decimal.Round(
						fPriceAlteration(initialPrice,(initialPrice * minOrMaxPricePercentage)),
						prodInfo.priceNumDecimalPlaces
					);
			}

			Func<OrderBookLevel2, decimal, decimal> fGetOurPrice = (ob, currentPrice) =>
			{

				// For testing
				//currentPrice = fPriceAlteration(currentPrice,1); //sidtodo remove

				var bestPrice = fBestPrice(ob);
				if (minOrMaxPrice == 0) minOrMaxPrice = fMinOrMaxPrice(bestPrice);

				bool weAreCurrentlyTheBestPrice = (currentPrice == bestPrice);

				decimal thePrice;
				if (numUnderCutUnits == 1)
				{
					thePrice = fPriceAlteration(bestPrice, prodInfo.smallestPriceDivision);
				}
				else
				{
					double powValue;
					int divideBy;
					if ((numUnderCutUnits % 2) != 0)
					{
						divideBy = 1;
						powValue = numUnderCutUnits - 2;
					}
					else
					{
						divideBy = 2;
						powValue = numUnderCutUnits - 1;
					}

					thePrice = Decimal.Round(fPriceAlteration(bestPrice, (((decimal)Math.Pow(10, powValue) * underCutUnit) / divideBy)),
						prodInfo.priceNumDecimalPlaces);
					if (thePrice == bestPrice || !fIsABetterPrice(thePrice, bestPrice))
					{
						thePrice = fPriceAlteration(bestPrice, prodInfo.smallestPriceDivision);
					}
				}

				// Don't under/overcut ourselves
				if (weAreCurrentlyTheBestPrice && fIsABetterPrice(thePrice, currentPrice))
				{
					thePrice = currentPrice;
				}

				if (weAreCurrentlyTheBestPrice && fIsABetterPrice(currentPrice, bestPrice))
				{
					var now = DateTime.Now;
					if ((now - fullChangeTs).TotalMilliseconds < 3000)
					{
						thePrice = currentPrice; // No change.
					}
					else
					{
						fullChangeTs = now;
					}
				}

				// Min/max
				if (fIsABetterPrice(thePrice, minOrMaxPrice))
				{
					thePrice = minOrMaxPrice;
				}
				Debug.Assert(!fIsABetterPrice(thePrice, minOrMaxPrice));

				// For testing..
				//sidtodo remove the entire block
				//if (orderSide == OrderSide.Sell)
				//{
				//	thePrice += 1;
				//}
				//else
				//{
				//	thePrice -= 1;
				//}
				//Debug.Assert(!fIsABetterPrice(thePrice, bestPrice));

				return thePrice;
			};

			return fGetOurPrice;
		}

		private static async Task<IEnumerable<string>> BuySellLimit(string[] cmdSplit,
			Action<Exception> HandleExceptions,
			ProductType product, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd,
			WebSocketState webSocketState, Action<bool> fEnableNetworkTraffic,
			Accounts.Models.Account account, string minMaxPriceErrorString,
			ProductInfo prodInfo, OrderSide orderSide, Func<OrderBookLevel2, decimal> fBestPrice,
			Func<decimal,decimal,string> fSuccessMsg,Func<decimal,decimal,decimal> fAmountToTrade)
		{

			using (await inProgressCmd.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			using (await webSocketState.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			{
				//// Check no tasks already in progress
				if (inProgressCmd.Ref != null)
				{
					//sidtodo generic in progress message
					return new string[] { "Error: A task is already in progress." };
				}

				if (cbClient.WebSocket.State != WebSocket4Net.WebSocketState.Closed &&
					cbClient.WebSocket.State != WebSocket4Net.WebSocketState.None)
				{
					return new string[] { "Error: Websocket is already in use." };
				}

				//// Amount to spend
				decimal amountToSpend;
				IEnumerable<string> rv = Account.GetAmountToSpendCmdLine(account, cmdSplit, 1, out amountToSpend);
				if (rv != null) return rv;

				int numUnderOvercutUnits = 1;
				if (cmdSplit.Length >= 3)
				{
					if (!int.TryParse(cmdSplit[2], out numUnderOvercutUnits) || numUnderOvercutUnits <= 0 ||
						numUnderOvercutUnits > 100)
					{
						return new string[] { $"Invalid undercut/overcut amount: {cmdSplit[2]}." };
					}
				}

				//// Min/max price. i.e. don't go above or below this level depending on whether it's buy or sell
				decimal minMaxPricePercentage = 0;
				decimal minMaxPrice = 0;
				var lastTrade = (await cbClient.ProductsService.GetTradesAsync(product, 1, 1))[0][0];

				string[] minMaxPriceErrors =
					GetPriceOrPercentageCmdLine(cmdSplit, 3, minMaxPriceErrorString, lastTrade.Price, 5, 1, out minMaxPrice, out minMaxPricePercentage);
				if (minMaxPriceErrors != null) return minMaxPriceErrors;

				//// Start the order book
				WatchOrderBookRes wobRes = OrderBook.WatchOrderBook(cbClient, product, HandleExceptions, null,
					null, webSocketState);

				//// Buy/sell task

				Func<OrderBookLevel2, decimal, decimal> fGetOurPrice =
					LimitTaskGetOurPrice(numUnderOvercutUnits, prodInfo, fBestPrice, orderSide, minMaxPrice,
					minMaxPricePercentage);

				var sync = new LimitTaskSynchronisation();

				Func<string> fSuccessMsgLocal = ()=>fSuccessMsg(sync.currentAmountToTrade, sync.currentPrice);

				Action fOnSuccess = async () =>
				{
					await wobRes.Stop();
					fEnableNetworkTraffic(true);
					// Clear the inprogress task
					await inProgressCmd.Clear();
					EventOutput(new EventOutput[] { new EventOutput(fSuccessMsgLocal(), null) });
				};

				var limitTaskCancelTS = new CancellationTokenSource();

				Func<decimal, decimal> fAmountToTradeLocal = (price) => fAmountToTrade(amountToSpend, price);

#pragma warning disable 4014
				Task.Run(async () => await LimitTask(fAmountToTradeLocal, wobRes.GetOrderBook,
						EventOutput, prodInfo, cbClient,
						fGetOurPrice, orderSide, fOnSuccess, fBestPrice, sync, limitTaskCancelTS.Token),
					limitTaskCancelTS.Token);
#pragma warning restore 4014

				//// Cancellation

				Func<Task<string[]>> fCancel = async () =>
				{
					using (await sync.theLock.LockAsync(Timeout.InfiniteTimeSpan))
					{
						limitTaskCancelTS.Cancel();
						await wobRes.Stop();
						fEnableNetworkTraffic(true);

						if (sync.currentOrderId != null)
						{

							Tuple<bool, bool> cancelRes;
							while ((cancelRes = await CancelOrderOneAttempt(sync.currentOrderId, cbClient)).Item2)
							{
							}

							if (!cancelRes.Item1)
							{
								// Bought/Sold successfully...
								return new string[] { fSuccessMsgLocal() };
							}
						}

					};

					return new string[] { "Successfully cancelled buy/sell task." };
				};

				inProgressCmd.Ref = new InProgressCommand();
				inProgressCmd.Ref.fCancel = fCancel;

				// Stop network traffic interfering with the trade
				fEnableNetworkTraffic(false);
			}

			return new string[] { "In progress..." };
		}

		// SELLM [coins]
		private static async Task<IEnumerable<string>> SellMarket(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd,
			WebSocketState webSocketState, Action<bool> fEnableNetworkTraffic)
		{

			var prod = getActiveProduct();
			var prodInfo = Products.productInfo[prod];

			var destCurrencyId = Currency.CurrencyId(prodInfo.destCurrency);

			var destAccount = await cbClient.AccountsService.GetAccountByIdAsync(destCurrencyId);

			using (await inProgressCmd.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			{
				//// Check no tasks already in progress
				if (inProgressCmd.Ref != null)
				{
					//sidtodo generic in progress message
					return new string[] { "Error: A task is already in progress." };
				}

				decimal amountToSpend;
				IEnumerable<string> rv = Account.GetAmountToSpendCmdLine(destAccount, cmdSplit, 1, out amountToSpend);
				if (rv != null) return rv;

				try
				{
					if (!await Orders.SellMarket(cbClient, amountToSpend, prod, HandleExceptions, true))
					{
						return new string[] { "No internet" };
					}

					return new string[] { "Success." };
				}
				catch (Exception e)
				{
					return new string[] { e.Message };
				}
			}
		}

		// BUYTHEDIP <funds/all/half/third/quarter> <max price> [optional rebuy price]
		private static async Task<IEnumerable<string>> BuyTheDip(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd,
			WebSocketState webSocketState, Action<bool> fEnableNetworkTraffic)
		{
			if (cmdSplit.Length != 3 && cmdSplit.Length != 4)
			{
				return new string[] { $"Invalid parameters: {s_BuyTheDipCmdLine}"};
			}

			//// Amount to spend
			var prod = getActiveProduct();
			var prodInfo = Products.productInfo[prod];

			var currencyId = Currency.CurrencyId(prodInfo.sourceCurrency);

			var account = await cbClient.AccountsService.GetAccountByIdAsync(currencyId);

			decimal amountToSpend;
			IEnumerable<string> rv = Account.GetAmountToSpendCmdLine(account, cmdSplit, 1, out amountToSpend);
			if (rv != null) return rv;

			//// Check no tasks already in progress
			using (await inProgressCmd.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			using (await webSocketState.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			{
				if (inProgressCmd.Ref != null)
				{
					//sidtodo generic in progress message
					return new string[] { "Error: A task is already in progress." };
				}

				if (cbClient.WebSocket.State != WebSocket4Net.WebSocketState.Closed &&
					cbClient.WebSocket.State != WebSocket4Net.WebSocketState.None)
				{
					return new string[] { "Error: Websocket is already in use." };
				}
				
				////// Min fall percentage
				//var minFallPercentageStr = cmdSplit[2];
				//decimal minFallPercentage = 0.01M; // 1%
				//if (!StringCompareNoCase(minFallPercentageStr, "DEF") &&
				//	!StringCompareNoCase(minFallPercentageStr, "DEFAULT"))
				//{
				//	if (!Decimal.TryParse(minFallPercentageStr, out minFallPercentage) || minFallPercentage<=0.5M || minFallPercentage>=10)
				//	{
				//		return new string[] { $"Invalid minFallPercentageStr {minFallPercentageStr}: {s_BuyTheDipCmdLine}" };
				//	}
				//	minFallPercentage /= 100;
				//}

				//// Max price
				decimal maxPrice;
				rv = GetPriceCmdLine(cmdSplit, 2, "max price", out maxPrice);
				if (rv != null) return rv;

				//// Rebuy price
				decimal optRebuyPrice=0;
				if (cmdSplit.Length == 4)
				{
					rv = GetPriceCmdLine(cmdSplit, 3, "rebuy price", out optRebuyPrice);
					if (rv != null) return rv;
					if (optRebuyPrice <= maxPrice)
					{
						return new string[] {
							$"Rebuy price is less than the max price, this makes no sense (do you know what you're doing??): {s_BuyTheDipCmdLine}"};
					}
				}

				//// Start watching the order book
				WatchOrderBookRes wobRes = OrderBook.WatchOrderBook(cbClient, prod, HandleExceptions, null,
					null, webSocketState);

				//// Start the buy the dip thread
				var taskCancelTs = new CancellationTokenSource();

#pragma warning disable 4014
				Task.Run(async () => await BuyTheDipTask(amountToSpend, wobRes,
						EventOutput, cbClient, taskCancelTs.Token, fEnableNetworkTraffic, () => inProgressCmd.Clear(),
						prod, maxPrice, optRebuyPrice, HandleExceptions),
					taskCancelTs.Token);
#pragma warning restore 4014

				Func<Task<string[]>> fCancel = async () =>
				{
					taskCancelTs.Cancel();
					await wobRes.Stop();
					fEnableNetworkTraffic(true);

					return new string[] { "Successfully cancelled buy the dip task." };
				};

				inProgressCmd.Ref = new InProgressCommand();
				inProgressCmd.Ref.fCancel = fCancel;
			}

			return new string[] { "In progress.." };
		}

		async static Task BuyTheDipTask(decimal amountToTrade, WatchOrderBookRes wob,
			EventOutputter EventOutput, CoinbaseProClient cbClient,
			CancellationToken ct, Action<bool> fEnableNetworkTraffic,
			Func<Task> fClearInProgressCmd, ProductType product, decimal maxPrice,
			decimal optRebuyPrice, Action<Exception> fHandleExceptions)
		{

			Action<string,bool> fOutputSingleLine = (text,speak) =>
			{
				EventOutput(new EventOutput[] { new EventOutput(text,null)});
				if (speak) Library.AsyncSpeak(text);
			};

			ProductInfo prodInfo = Products.productInfo[product];

			Func<decimal,decimal,Task> fDoTheBuy = async (amount, price) =>
			{
				fEnableNetworkTraffic(false);

				try
				{
					await RepeatUntilHaveInternet(() => Orders.BuyMarket(cbClient, amount, product, fHandleExceptions, true));
					var text = $"Successful market buy at {prodInfo.fSpeakPrice(price)}.";
					fOutputSingleLine(text, true);
				}
#pragma warning disable 168
				catch (Exception e)
#pragma warning restore 168
				{
					Library.AsyncSpeak($"Failed to market buy due to an unexpected exception.");
				}

				await wob.Stop();

				await fClearInProgressCmd();

				fEnableNetworkTraffic(true);
			};

			await Task.Delay(1000); // Give the order book enough time to kick in

			var fiveMinCandle=new CandleState(1*60); //sidtodo change to 5 mins
			var tenMinCandle= new CandleState(10*60);
			var fifteenMinCandle=new CandleState(15*60);

			CandleState dipCandle = null;

			decimal previousBestPrice = 0;
			decimal[] triggerPriceArray = new decimal[] { optRebuyPrice};

			do
			{
				if (ct.IsCancellationRequested) return;

				var ob = wob.GetOrderBook();

				if (ct.IsCancellationRequested) return;

				if (ob != null)
				{
					DateTime now = DateTime.Now;
					decimal bestPrice = GetSellBestPrice(ob);
					Candle.AddNewPrice(fiveMinCandle, bestPrice, now);
					Candle.AddNewPrice(tenMinCandle, bestPrice, now);
					Candle.AddNewPrice(fifteenMinCandle, bestPrice, now);

					//fOutputSingleLine($"{fiveMinCandle.Low} {fiveMinCandle.High} {fiveMinCandle.startTime}"); //sidtodo remove

					decimal fiveMinCandlePriceDropPercentage = Candle.PriceDropPercentage(fiveMinCandle);
					decimal tenMinCandlePriceDropPercentage= Candle.PriceDropPercentage(tenMinCandle);
					decimal fifteenMinCandlePriceDropPercentage = Candle.PriceDropPercentage(tenMinCandle);

					fOutputSingleLine($"{Decimal.Round(fiveMinCandlePriceDropPercentage * 100,4)} {fiveMinCandle.High} {fiveMinCandle.Low}",false);

					//fOutputSingleLine($"{fiveMinCandlePriceDropPercentage*100} {tenMinCandlePriceDropPercentage * 100} "+
					//	$"{fifteenMinCandlePriceDropPercentage * 100}"); //sidtodo remove

					if(dipCandle==null)
					{

						//const decimal fiveMinCandleTriggerPercentage = 0.0075M; // 0.75%
						const decimal fiveMinCandleTriggerPercentage = 0.001M; // 0.1% //sidtodo comment

						if ((Candle.HaveFullDataset(fiveMinCandle) && fiveMinCandlePriceDropPercentage >= fiveMinCandleTriggerPercentage /* 0.75% */) ||
							(Candle.HaveFullDataset(tenMinCandle) && tenMinCandlePriceDropPercentage >= 0.013M /* 1.3% */) ||
							(Candle.HaveFullDataset(fifteenMinCandle) && fifteenMinCandlePriceDropPercentage >= 0.018M /* 1.8% */)
						)
						{
							fOutputSingleLine($"We are in a dip: {fiveMinCandlePriceDropPercentage} {tenMinCandlePriceDropPercentage} {fifteenMinCandlePriceDropPercentage}",true);

							dipCandle = new CandleState(30 /* Seconds duration rolling */);
						}
					}

					if (dipCandle != null)
					{
						Candle.AddNewPrice(dipCandle, bestPrice, now);
						// Price is going back up?
						//TODO potentially may want to check a percentage. i.e. high > open && %diff between high and open is 1% or something
						if (Candle.HaveFullDataset(dipCandle) && dipCandle.Open == dipCandle.Low && dipCandle.Open < dipCandle.Close)
						{
							fOutputSingleLine($"Price is going back up: low={dipCandle.Low} open={dipCandle.Open} close={dipCandle.Close}, bestPrice={bestPrice}", true);

							if (bestPrice < maxPrice)
							{
								await fDoTheBuy(000.1M, bestPrice); //sidtodo remove
								//await fDoTheBuy(amountToTrade); //sidtodo uncomment
								return;
							}

							// Clear the dip candle and wait for another dip or for it to dip further
							dipCandle = null;
						}
					}
					else if(optRebuyPrice>0)
					{
						// Check for rebuy
						if (CheckIfPriceIsTriggered(bestPrice,triggerPriceArray,previousBestPrice))
						{
							await fDoTheBuy(amountToTrade, bestPrice);
							return;
						}
					}

					previousBestPrice = bestPrice;
				}

				if (ct.IsCancellationRequested) return;
				await Task.Delay(500);
			} while (true);
		}

		// SELLMATPRICE [funds/all/half/third/quarter] [price CSV]
		private static async Task<IEnumerable<string>> SellMarketAtPrice(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd,
			WebSocketState webSocketState, Action<bool> fEnableNetworkTraffic)
		{
			var prod = getActiveProduct();
			var prodInfo = Products.productInfo[prod];

			Func<decimal, Task<bool>> fSellCmd = async (amountToSpend) =>
			{
				return await Orders.SellMarket(cbClient, amountToSpend, prod, HandleExceptions, true);
			};

			// Note we are using 'GetBuyBestPrice' because that's the price we will be SELLING at
			return await BuySellMarketAtPrice(inProgressCmd, prodInfo.destCurrency,
				prodInfo.volNumDecimalPlaces,
				cmdSplit, cbClient, prod, HandleExceptions, webSocketState, EventOutput,
				OrderSide.Sell, GetBuyBestPrice, fEnableNetworkTraffic, fSellCmd, "sell");
		}

		// BUYMATPRICE [funds/all/half/third/quarter] [price CSV]
		private static async Task<IEnumerable<string>> BuyMarketAtPrice(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd,
			WebSocketState webSocketState, Action<bool> fEnableNetworkTraffic)
		{
			var prod = getActiveProduct();
			var prodInfo = Products.productInfo[prod];

			Func<decimal,Task<bool>> fBuyCmd = async (amountToSpend) =>
			{
				return await Orders.BuyMarket(cbClient, amountToSpend, prod, HandleExceptions, true);
			};

			// Note we are using 'GetSellBestPrice' because that's the price we will be BUYING at
			return await BuySellMarketAtPrice(inProgressCmd, prodInfo.sourceCurrency, prodInfo.priceNumDecimalPlaces,
				cmdSplit, cbClient, prod, HandleExceptions, webSocketState, EventOutput,
				OrderSide.Buy, GetSellBestPrice, fEnableNetworkTraffic, fBuyCmd, "buy");
		}

		private static async Task<IEnumerable<string>> BuySellMarketAtPrice(
			LockedByRef<InProgressCommand> inProgressCmd, Types.Currency currency, int amountTruncatePlaces,
			string[] cmdSplit, CoinbaseProClient cbClient, ProductType product,
			Action<Exception> fHandleExceptions, WebSocketState webSocketState,
			EventOutputter EventOutput, OrderSide orderSide, Func<OrderBookLevel2, decimal> fGetBestPrice,
			Action<bool> fEnableNetworkTraffic, Func<decimal, Task<bool>> fTradeCmd,
			string buySellStr)
		{

			//// Amount to trade
			var currencyId = Currency.CurrencyId(currency);

			var account = await cbClient.AccountsService.GetAccountByIdAsync(currencyId);

			decimal amountToSpend;
			IEnumerable<string> rv = Account.GetAmountToSpendCmdLine(account, cmdSplit, 1, out amountToSpend);
			if (rv != null) return rv;

			//// Validate prices
			if (cmdSplit.Length <= 2)
			{
				return new string[] { $"Price(s) not specified: {cmdSplit[0]} [funds/all/half/third/quarter] [price CSV]" };
			}
			var priceStrArray = cmdSplit[2].Split(',');
			var priceArray = new List<decimal>();
			for (int i = 0; i < priceStrArray.Length; ++i)
			{
				var trimmedPriceStr = priceStrArray[i].Trim();
				if (trimmedPriceStr == "") continue;

				decimal iterPrice;
				if (!Decimal.TryParse(trimmedPriceStr, out iterPrice))
				{
					return new string[] { $"Invalid price: {trimmedPriceStr}"};
				}
				priceArray.Add(iterPrice);
			}
			if (priceArray.Count == 0) return new string[] { $"Invalid price CSV: {cmdSplit[2]}" };

			using (await inProgressCmd.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			using (await webSocketState.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			{
				//// Check no tasks already in progress
				if (inProgressCmd.Ref != null)
				{
					//sidtodo generic in progress message
					return new string[] { "Error: A task is already in progress." };
				}

				if (cbClient.WebSocket.State != WebSocket4Net.WebSocketState.Closed &&
					cbClient.WebSocket.State != WebSocket4Net.WebSocketState.None)
				{
					return new string[] { "Error: Websocket is already in use." };
				}

				//// Start the order book
				WatchOrderBookRes wobRes = OrderBook.WatchOrderBook(cbClient, product, fHandleExceptions, null,
					null, webSocketState);

				var taskCancelTs = new CancellationTokenSource();

#pragma warning disable 4014
				var buySellTask = Task.Run(async () => await BuySellMarketAtPriceTask(amountToSpend, wobRes,
						EventOutput, cbClient, orderSide, taskCancelTs.Token, priceArray, fGetBestPrice,
						fEnableNetworkTraffic, fTradeCmd, () => inProgressCmd.Clear(), buySellStr,
						product),
					taskCancelTs.Token);
#pragma warning restore 4014

				Func<Task<string[]>> fCancel = async () =>
				{
					taskCancelTs.Cancel();
					await wobRes.Stop();
					fEnableNetworkTraffic(true);

					return new string[] { "Successfully cancelled buy/sell task." };
				};

				inProgressCmd.Ref = new InProgressCommand();
				inProgressCmd.Ref.fCancel = fCancel;
			}

			return new string[] { "In progress.."};
		}

		async static Task BuySellMarketAtPriceTask(decimal amountToTrade, WatchOrderBookRes wob,
			EventOutputter EventOutput, CoinbaseProClient cbClient,
			OrderSide orderSide,CancellationToken ct, IEnumerable<decimal> priceArray,
			Func<OrderBookLevel2, decimal> fGetBestPrice,
			Action<bool> fEnableNetworkTraffic, Func<decimal, Task<bool>> fTradeCmd,
			Func<Task> fClearInProgressCmd, string buySellStr,
			ProductType product)
		{

			await Task.Delay(1000); // Give the order book enough time to kick in

			decimal previousBestPrice = 0;

			ProductInfo prodInfo = Products.productInfo[product];

			do
			{
				if (ct.IsCancellationRequested) return;

				var ob = wob.GetOrderBook();

				if (ct.IsCancellationRequested) return;

				if (ob != null)
				{
					decimal curBestPrice = fGetBestPrice(ob);

					if(CheckIfPriceIsTriggered(curBestPrice, priceArray, previousBestPrice))
					{
						fEnableNetworkTraffic(false);

						try
						{
							await RepeatUntilHaveInternet(()=> fTradeCmd(amountToTrade));
							var text = $"Successful market {buySellStr} at {prodInfo.fSpeakPrice(curBestPrice)}.";
							EventOutput(new EventOutput[] {new EventOutput(text,null) });
							Library.AsyncSpeak(text);
						}
#pragma warning disable 168
						catch (Exception e)
#pragma warning restore 168
						{
							Library.AsyncSpeak($"Failed to market {buySellStr} due to an unexpected exception.");
						}

						await wob.Stop();
						fEnableNetworkTraffic(true);
						await fClearInProgressCmd();
						return;
					}
					previousBestPrice = curBestPrice;
				}

				await Task.Delay(500);
			} while (true);
		}


		private static bool CheckIfPriceIsTriggered(
			decimal curBestPrice, IEnumerable<decimal> triggerPriceArray,
			decimal previousBestPrice)
		{
			if (previousBestPrice != 0 && previousBestPrice != curBestPrice)
			{
				var priceHasIncreased = (curBestPrice > previousBestPrice);
				foreach (var iterTriggerPrice in triggerPriceArray)
				{
					bool priceIsTriggered;
					if (priceHasIncreased)
					{
						priceIsTriggered = (curBestPrice >= iterTriggerPrice && iterTriggerPrice > previousBestPrice);
					}
					else
					{
						priceIsTriggered = (curBestPrice <= iterTriggerPrice && iterTriggerPrice < previousBestPrice);
					}

					if (priceIsTriggered)
					{
						return true;
					}
				}
			}
			return false;
		}

		// BUYM [funds]
		private static async Task<IEnumerable<string>> BuyMarket(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd,
			WebSocketState webSocketState, Action<bool> fEnableNetworkTraffic)
		{

			var prod = getActiveProduct();
			var prodInfo = Products.productInfo[prod];

			var srcCurrencyId = Currency.CurrencyId(prodInfo.sourceCurrency);

			var srcAccount = await cbClient.AccountsService.GetAccountByIdAsync(srcCurrencyId);

			using (await inProgressCmd.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			{
				//// Check no tasks already in progress
				if (inProgressCmd.Ref != null)
				{
					//sidtodo generic in progress message
					return new string[] { "Error: A task is already in progress." };
				}

				decimal amountToSpend;
				IEnumerable<string> rv = Account.GetAmountToSpendCmdLine(srcAccount, cmdSplit, 1, out amountToSpend);
				if (rv != null) return rv;

				try
				{
					if (!await Orders.BuyMarket(cbClient, amountToSpend, prod, HandleExceptions, true))
					{
						return new string[] { "No internet" };
					}

					return new string[] { "Success." };
				}
				catch (Exception e)
				{
					return new string[] { e.Message };
				}
			}
		}

		private static decimal GetSellBestPrice(OrderBookLevel2 ob)
		{
			return ob.asks.ElementAt(0).Key;
		}

		// SELLL [coins] [under/over cut units: 1-10000] [min price/%]
		private static async Task<IEnumerable<string>> SellLimit(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd,
			WebSocketState webSocketState, Action<bool> fEnableNetworkTraffic)
		{
			
			var prod = getActiveProduct();
			var prodInfo = Products.productInfo[prod];

			var destCurrencyId = Currency.CurrencyId(prodInfo.destCurrency);

			var destAccount = await cbClient.AccountsService.GetAccountByIdAsync(destCurrencyId);

			Func<decimal,decimal,string> fSuccessMsg = (amount, price) => $"Sold {amount} {prodInfo.spokenName} at " +
					$"{prodInfo.fSpeakPrice(price)}";

			Func<decimal,decimal,decimal> fAmountToSpend =
				(currencyToSpend,price) => TruncateRound(currencyToSpend,prodInfo.volNumDecimalPlaces);

			return await BuySellLimit(cmdSplit, HandleExceptions,
				prod, cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic,
				destAccount, "minimum", prodInfo, OrderSide.Sell, GetSellBestPrice, fSuccessMsg, fAmountToSpend);
		}

		static bool IsInvalidOrderId(Exception e)
		{
			var msgLower = e.Message.ToLower();
			bool rv=
				(msgLower.IndexOf("invalid") >= 0 ||
				msgLower.IndexOf("not found") >= 0
			);
			return rv;
		}	
		
		static async Task<GetOrderRes> GetOrder(
			string serverOrderId, CoinbaseProClient cbClient)
		{
			try
			{
				var res = await cbClient.OrdersService.GetOrderByIdAsync(serverOrderId);
				return new GetOrderRes(res,false);
			}
			catch (Exception e)
			{
				bool errorSending = !IsInvalidOrderId(e);
				return new GetOrderRes(null, errorSending);
			}
		}

		static async Task<Tuple<bool,bool>> CancelOrderOneAttempt(string orderId,
			CoinbaseProClient cbClient)
		{
			bool error = false;
			try
			{
				await cbClient.OrdersService.CancelOrderByIdAsync(orderId);
			}
			catch (Exception e)
			{
				bool isInvalidOrderId = IsInvalidOrderId(e);
				if (isInvalidOrderId)
				{
					return new Tuple<bool,bool>(false,false);
				}

				error = true;
			}

			return new Tuple<bool, bool>(!error, error);
		}

		//sidtodo here complain about no internet
		async static Task LimitTask(Func<decimal,decimal> fAmountToTrade, Func<OrderBookLevel2> getOrderBook,
			EventOutputter EventOutput, ProductInfo prodInfo, CoinbaseProClient cbClient,
			Func<OrderBookLevel2, decimal, decimal> fGetOurPrice, OrderSide orderSide,
			Action fOnSuccess, Func<OrderBookLevel2, decimal> fBestPrice,
			LimitTaskSynchronisation synchronisation,CancellationToken ct)
		{

			await Task.Delay(1000); // Give the order book enough time to kick in

			decimal ourCurrentPrice = 0;

			do
			{

				var ob = getOrderBook();
				bool setThePrice = false;

				DateTime updatePriceTime = DateTime.Now;

				bool errorSending = false;

				if (ob != null)
				{

					decimal newPrice = fGetOurPrice(ob, ourCurrentPrice);

					bool cancelThePrice = (ourCurrentPrice != 0 && newPrice != ourCurrentPrice);
					setThePrice = (ourCurrentPrice == 0 || newPrice != ourCurrentPrice);
					
					using (await synchronisation.theLock.LockAsync(Timeout.InfiniteTimeSpan))
					{
						if (ct.IsCancellationRequested) return;

						bool orderHasBeenFilled = false;

						if (cancelThePrice)
						{

							var cancelRes = await CancelOrderOneAttempt(synchronisation.currentOrderId, cbClient);
							orderHasBeenFilled = (!cancelRes.Item1 && !cancelRes.Item2);

							if (!orderHasBeenFilled)
							{
								errorSending = cancelRes.Item2;
								if (!errorSending) synchronisation.currentOrderId = null;
							}
						}
						else if(synchronisation.currentOrderId!=null)
						{
							var getOrderRes = await GetOrder(synchronisation.currentOrderId, cbClient);
							errorSending = getOrderRes.Item2;
							orderHasBeenFilled = !errorSending && getOrderRes.Item1 == null;
						}

						if (orderHasBeenFilled)
						{
							// Success
							synchronisation.currentOrderId = null;
							fOnSuccess();
							return;
						}

						if (ct.IsCancellationRequested) return;

						if (setThePrice && !errorSending)
						{
							decimal currentAmountToTrade = fAmountToTrade(newPrice);
							try
							{
								var orderResponse = await cbClient.OrdersService.PlaceLimitOrderAsync(
									orderSide, prodInfo.productType,
									currentAmountToTrade, newPrice, TimeInForce.Gtc, true);

								updatePriceTime = DateTime.Now;
								synchronisation.currentOrderId = orderResponse.Id.ToString();
								synchronisation.currentPrice = newPrice;
								synchronisation.currentAmountToTrade = currentAmountToTrade;

								ourCurrentPrice = newPrice;

								EventOutput(new EventOutput[] { new EventOutput($"{fBestPrice(ob)} - {ourCurrentPrice}", null) });
							}
#pragma warning disable 168
							catch (Exception e)
#pragma warning restore 168
							{
								//sidtodo !!!!!!!!!!!!!!!!!!!!!!!!!!!!! output insufficient funds, adn insufficient size errors
								errorSending = true;
							}

						}

						if (ct.IsCancellationRequested) return;
					}
				}

				if (ct.IsCancellationRequested) return;

				int sleepMs = 1000;
				if (errorSending)
				{
					sleepMs = 1000;
				}
				else if (setThePrice)
				{
					sleepMs = 1000 - (int)(DateTime.Now - updatePriceTime).TotalMilliseconds;
				}

				if (sleepMs > 0)
				{
					await Task.Delay(sleepMs);
				}
			} while (true);
		}

		private static decimal GetBuyBestPrice(OrderBookLevel2 ob)
		{
			return ob.bids.ElementAt(ob.bids.Count - 1).Key;
		}

		// BUYL [funds] [under/overcut amount] [max price]
		private static async Task<IEnumerable<string>> BuyLimit(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd,
			WebSocketState webSocketState,Action<bool> fEnableNetworkTraffic)
		{

			var prod = getActiveProduct();
			var prodInfo = Products.productInfo[prod];

			var sourceCurrencyId= Currency.CurrencyId(prodInfo.sourceCurrency);

			var sourceAccount = await cbClient.AccountsService.GetAccountByIdAsync(sourceCurrencyId);

			Func<decimal, decimal, string> fSuccessMsg = (amount, price) => $"Bought {amount} {prodInfo.spokenName} at " +
					  $"{prodInfo.fSpeakPrice(price)}";
			
			Func<decimal, decimal, decimal> fAmountToBuy = (currencyToSpend, price) =>
			{
				decimal amount= TruncateRound(currencyToSpend / price, prodInfo.volNumDecimalPlaces);
				return amount;
			};

			return await BuySellLimit(cmdSplit, HandleExceptions,
				prod, cbClient, EventOutput, inProgressCmd, webSocketState, fEnableNetworkTraffic,
				sourceAccount, "maximum", prodInfo, OrderSide.Buy, GetBuyBestPrice, fSuccessMsg, fAmountToBuy);

		}

		// tw showsummary [start date] <start time> [end date] <end time>
		private static async Task<IEnumerable<string>> ShowSummary(string[] cmdSplit,
			TradeHistoryState tradeHistoryState,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient)
		{

			if (cmdSplit.Length != 6 && cmdSplit.Length!=4) return new string[] { $"Invalid parameters: {s_ShowSummaryCmdLine}"};

			bool includeDate = cmdSplit.Length == 6;

			DateTime? startTime = ReadTimestamp(includeDate, cmdSplit, 2);
			if(startTime==null) return new string[] { "Invalid start time" };

			DateTime? endTime = ReadTimestamp(includeDate, cmdSplit, ((includeDate)?4:3));
			if (endTime == null) return new string[] { "Invalid end time" };

			var product = getActiveProduct();
			return await TradeHistory.ShowSummary(product, HandleExceptions, cbClient, tradeHistoryState,
				startTime.Value, endTime.Value, getProdStats()[product]);
		}

		private static async Task<IEnumerable<string>> ShowTrades(string[] cmdSplit,
			TradeHistoryState tradeHistoryState, 
			Action<Exception> HandleExceptions, CoinbaseProClient cbClient,
			Func<decimal,decimal,bool> includeOnlyOptional, Func<ProductType> getActiveProduct)
		{
			int numPages;
			if (int.TryParse(cmdSplit[2], out numPages))
			{
				return await TradeHistory.GetTrades(getActiveProduct(),numPages, HandleExceptions, cbClient,
					includeOnlyOptional);
			}
			else
			{
				return new string[] { "Invalid number of pages." };
			}
		}

		private static IEnumerable<string> ShowLevel(string[] cmdSplit,
			TradeHistoryState tradeHistoryState, Func<ProductStatsDictionary> getProdStats,
			bool extended, ProductType productType)
		{
			int level;
			if (int.TryParse(cmdSplit[2], out level))
			{

				var prodStatsDictionary = getProdStats();
				ProductStats prodStats=null;
				if (prodStatsDictionary != null)
				{
					prodStatsDictionary.TryGetValue(productType, out prodStats);
				}

				return TradeHistory.DescribeSummaryLevelHistory(tradeHistoryState, level, prodStats,
					productType, extended);
			}
			else
			{
				return new string[] { "Invalid level." };
			}
		}

		private static void Test2(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Func<ProductStatsDictionary> getProductStats,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers)
		{

			var prodStats = getProductStats();
			var prod = ProductType.LinkGbp;
			var linkStats = prodStats[prod];

			// Check that the bull run triggers on size > 8% of 24 hour volume
			CBProductTrade[] trades =
			{
				new CBProductTrade(){Price=15.81626M, Size=linkStats.Volume*0.09M,Time=new DateTime(2021,01,19,16,40,26)},
				new CBProductTrade(){Price=15.82778M, Size=1.48M,Time=new DateTime(2021,01,19,16,40,52)},
				new CBProductTrade(){Price=15.81609M, Size=100,Time=new DateTime(2021,01,19,16,41,32)},
			};

			TradeHistory.Test(cbClient, EventOutput, trades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}

		// Test price going consistently up trigger
		private static void Test4(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers,
			Func<ProductStatsDictionary> getProductStats)
		{

			CBProductTrade[] trades =
			{
				new CBProductTrade(){Price=15.81626M, Size=1002M, Side=OrderSide.Buy, Time=new DateTime(2021,01,19,16,40,26)},
				new CBProductTrade(){Price=15.80626M, Size=1M, Side=OrderSide.Sell, Time=new DateTime(2021,01,19,16,40,30)},
				new CBProductTrade(){Price=15.82778M, Size=1003M, Side=OrderSide.Buy, Time=new DateTime(2021,01,19,16,56,52)},
				new CBProductTrade(){Price=15.90M, Size=100, Side=OrderSide.Buy, Time=new DateTime(2021,01,19,16,57,32)},
				new CBProductTrade(){Price=15.91M, Size=100, Side=OrderSide.Sell, Time=new DateTime(2021,01,19,17,13,32)},
			};

			var prod = ProductType.LinkGbp;

			TradeHistory.Test(cbClient, EventOutput, trades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}

		// Test price going consistently down trigger
		private static void Test5(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers,
			Func<ProductStatsDictionary> getProductStats)
		{

			CBProductTrade[] trades =
			{
				new CBProductTrade(){Price=15.81626M, Size=1002M, Side=OrderSide.Sell, Time=new DateTime(2021,01,19,16,40,26)},
				new CBProductTrade(){Price=15.80626M, Size=1M, Side=OrderSide.Buy, Time=new DateTime(2021,01,19,16,40,30)},
				new CBProductTrade(){Price=15.82778M, Size=1003M, Side=OrderSide.Sell, Time=new DateTime(2021,01,19,16,56,52)},
				new CBProductTrade(){Price=15.90M, Size=100, Side=OrderSide.Sell, Time=new DateTime(2021,01,19,16,57,32)},
				new CBProductTrade(){Price=15.91M, Size=100, Side=OrderSide.Buy, Time=new DateTime(2021,01,19,17,13,32)},
			};

			var prod = ProductType.LinkGbp;

			TradeHistory.Test(cbClient, EventOutput, trades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}

		// Test rapid price decrease trigger using recent trades
		private static void Test6(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers,
			Func<ProductStatsDictionary> getProductStats)
		{

			decimal startPrice = 20.00M;

			CBProductTrade[] trades1 =
			{
				new CBProductTrade(){Price=startPrice, Size=1002M, Side=OrderSide.Buy},
				new CBProductTrade(){Price=startPrice-(startPrice*0.0025M), Size=1M, Side=OrderSide.Buy},
				new CBProductTrade(){Price=startPrice-(startPrice*0.0051M), Size=1003M, Side=OrderSide.Buy},
			};

			CBProductTrade[] trades2 =
			{
				new CBProductTrade(){Price=startPrice-(startPrice*0.0052M), Size=1002M,
					Side =OrderSide.Buy},
				new CBProductTrade(){Price=startPrice-(startPrice*0.006M), Size=1002M,
					Side =OrderSide.Buy},
			};

			var pagedTrades = new List<IList<CBProductTrade>>();
			pagedTrades.Add(trades1);
			pagedTrades.Add(trades2);

			var prod = ProductType.LinkGbp;

			TradeHistory.Test(cbClient, EventOutput, pagedTrades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}

		// Test steady price increase
		private static void Test8(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers,
			Func<ProductStatsDictionary> getProductStats)
		{

			decimal startPrice = 20.00M;
			var now = DateTime.Now;

			CBProductTrade[] trades =
			{
				new CBProductTrade(){Price=startPrice, Size=1002M, Side=OrderSide.Sell, Time=now},
				new CBProductTrade(){Price=startPrice+(startPrice*0.0051M), Size=1M, Side=OrderSide.Sell, Time=now.AddMinutes(3)},
				new CBProductTrade(){Price=startPrice+(startPrice*0.0101M), Size=1003M, Side=OrderSide.Sell, Time=now.AddMinutes(6)},
				new CBProductTrade(){Price=startPrice+(startPrice*0.0101M), Size=1003M, Side=OrderSide.Sell, Time=now.AddMinutes(7)},
			};

			var prod = ProductType.LinkGbp;

			TradeHistory.Test(cbClient, EventOutput, trades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}

		// Test large buy/sell
		private static void Test9(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers,
			Func<ProductStatsDictionary> getProductStats)
		{

			decimal startPrice = 20.00M;
			var now = DateTime.Now;

			var prod = ProductType.LinkGbp;

			var prodStats = getProductStats()[prod];
			decimal twoPercentOf24HourVol = prodStats.Volume * 0.02M;

			OrderSide orderSide = OrderSide.Sell;

			CBProductTrade[] trades =
			{
				new CBProductTrade(){Price=startPrice, Size=1002M, Side=orderSide, Time=now},
				new CBProductTrade(){Price=startPrice+(startPrice*0.0051M), Size=twoPercentOf24HourVol/2, Side=orderSide, Time=now.AddSeconds(1)},
				new CBProductTrade(){Price=startPrice+(startPrice*0.0101M), Size=twoPercentOf24HourVol/2, Side=orderSide, Time=now.AddSeconds(2)},
				new CBProductTrade(){Price=startPrice+(startPrice*0.0101M), Size=1003M, Side=orderSide, Time=now.AddMinutes(1)},
			};

			TradeHistory.Test(cbClient, EventOutput, trades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}

		// Test rapid price increase trigger using recent trades
		private static void Test7(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers,
			Func<ProductStatsDictionary> getProductStats)
		{

			decimal startPrice = 20.00M;

			CBProductTrade[] trades =
			{
				new CBProductTrade(){Price=startPrice, Size=1002M, Side=OrderSide.Sell},
				new CBProductTrade(){Price=startPrice+(startPrice*0.0025M), Size=1M, Side=OrderSide.Sell},
				new CBProductTrade(){Price=startPrice+(startPrice*0.0051M), Size=1003M, Side=OrderSide.Sell},
			};

			var prod = ProductType.LinkGbp;

			TradeHistory.Test(cbClient, EventOutput, trades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}

		// Test price dropped below £15.50
		private static void Test3(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers,
			Func<ProductStatsDictionary> getProductStats)
		{

			CBProductTrade[] trades =
			{
				new CBProductTrade(){Price=15.81626M, Size=12.61M,Time=new DateTime(2021,01,19,16,40,26)},
				new CBProductTrade(){Price=15.80626M, Size=12.61M,Time=new DateTime(2021,01,19,16,40,30)},
				new CBProductTrade(){Price=15.82778M, Size=1.48M,Time=new DateTime(2021,01,19,16,41,52)},
				new CBProductTrade(){Price=15.48M, Size=100,Time=new DateTime(2021,01,19,16,42,32)},
				new CBProductTrade(){Price=15.47M, Size=100,Time=new DateTime(2021,01,19,16,43,32)},
			};

			var prod = ProductType.LinkGbp;

			TradeHistory.Test(cbClient, EventOutput, trades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}

		private static void Test1(CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			Action<Exception> HandleExceptions,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers,
			Func<ProductStatsDictionary> getProductStats)
		{

			CBProductTrade[] trades =
			{
				new CBProductTrade(){Price=15.81626M, Size=2100, Side=OrderSide.Buy, Time=new DateTime(2021,01,19,16,40,26)},
				new CBProductTrade(){Price=15.82778M, Size=1.48M, Side=OrderSide.Sell, Time=new DateTime(2021,01,19,16,46,52)},
				new CBProductTrade(){Price=15.82778M, Size=1.48M, Side=OrderSide.Sell, Time=new DateTime(2021,01,19,16,47,52)},
				new CBProductTrade(){Price=15.82778M, Size=1.48M, Side=OrderSide.Sell, Time=new DateTime(2021,01,19,16,48,52)},
			};

			var prod = ProductType.LinkGbp;

			TradeHistory.Test(cbClient, EventOutput, trades, slUpdateTriggers[prod],
				HandleExceptions, newTradesTriggers[prod], prod, getProductStats);
		}
	}
}
