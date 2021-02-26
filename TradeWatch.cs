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

		public static async Task<IEnumerable<string>> CmdLine(CoinbaseProClient cbClient,
			string[] cmdSplit, EventOutputter EventOutput,
			Func<ProductStatsDictionary> getProdStats,
			Action<Exception> HandleExceptions, TradeHistoryState tradeHistoryState,
			Dictionary<ProductType,NewTradesTriggerList> newTradesTriggers,
			Func<ProductType> getActiveProduct, Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			LockedByRef<InProgressCommand> inProgressCmd)
		{

			if (StringCompareNoCase(cmdSplit[0], "BUYL"))
			{
				return await BuyLimit(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd);
			}
			else if (StringCompareNoCase(cmdSplit[0], "SELLL"))
			{
				return await SellLimit(cmdSplit, HandleExceptions, getProdStats, getActiveProduct,
					cbClient, EventOutput, inProgressCmd);
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

				currentPrice = fPriceAlteration(currentPrice,1); //sidtodo remove

				var bestPrice = fBestPrice(ob);
				if (minOrMaxPrice == 0) minOrMaxPrice = fMinOrMaxPrice(bestPrice);

				decimal thePrice;
				if (numUnderCutUnits == 1)
				{
					thePrice = fPriceAlteration(bestPrice,prodInfo.smallestPriceDivision);
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

					thePrice = Decimal.Round(fPriceAlteration(bestPrice,(((decimal)Math.Pow(10, powValue) * underCutUnit) / divideBy)),
						prodInfo.priceNumDecimalPlaces);
					if (thePrice == bestPrice || !fIsABetterPrice(thePrice,bestPrice))
					{
						thePrice = fPriceAlteration(bestPrice, prodInfo.smallestPriceDivision);
					}
				}

				if(!fIsABetterPrice(thePrice, currentPrice) && fIsABetterPrice(currentPrice, bestPrice))
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

				//sidtodo remove this whole block
				if (orderSide == OrderSide.Sell)
				{
					thePrice += 1;
				}
				else
				{
					thePrice -= 1;
				}
				Debug.Assert(!fIsABetterPrice(thePrice,bestPrice)); //sidtodo remove

				return thePrice;
			};

			return fGetOurPrice;
		}

		// SELLL [coins] [under/over cut units: 1-10000] [min price/%]
		private static async Task<IEnumerable<string>> SellLimit(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd)
		{

			using (await inProgressCmd.theLock.LockAsync(Timeout.InfiniteTimeSpan))
			{

				if (inProgressCmd.Ref != null)
				{
					//sidtodo generic in progress message
					return new string[] { "Error: A task is already in progress." };
				}

				//sidtodo check task not already in progress.

				var prod = getActiveProduct();
				var prodInfo = Products.productInfo[prod];

				var destCurrencyId = Currency.CurrencyId(prodInfo.destCurrency);

				var destAccount = await cbClient.AccountsService.GetAccountByIdAsync(destCurrencyId);

				decimal amountToSpend;
				if (cmdSplit.Length >= 2 && !StringCompareNoCase(cmdSplit[1], "all"))
				{

					if (StringCompareNoCase(cmdSplit[1], "half"))
					{
						amountToSpend = destAccount.Available / 2;
					}
					else if (StringCompareNoCase(cmdSplit[1], "third"))
					{
						amountToSpend = destAccount.Available / 3;
					}
					else if (StringCompareNoCase(cmdSplit[1], "quater"))
					{
						amountToSpend = destAccount.Available / 4;
					}
					else
					{
						if (!Decimal.TryParse(cmdSplit[1], out amountToSpend) || amountToSpend <= 0)
						{
							return new string[] { $"Invalid amount: {cmdSplit[1]}." };
						}

						if ((destAccount.Available - amountToSpend) < 1) amountToSpend = destAccount.Available;

						if (amountToSpend > destAccount.Available)
						{
							return new string[] { $"{amountToSpend} is more than is available "+
								$"({destAccount.Available})." };
						}
					}
				}
				else
				{
					amountToSpend = destAccount.Available;
				}

				amountToSpend = 1; //sidtodo remove

				int numUnderCutUnits = 1;
				if (cmdSplit.Length >= 3)
				{
					if (!int.TryParse(cmdSplit[2], out numUnderCutUnits) || numUnderCutUnits <= 0 || numUnderCutUnits > 100)
					{
						return new string[] { $"Invalid undercut amount: {cmdSplit[2]}." };
					}
				}

				// Min price. i.e. don't go below this level
				decimal minPricePercentage = 0;
				decimal minPrice = 0;
				var lastTrade = (await cbClient.ProductsService.GetTradesAsync(prod, 1, 1))[0][0];

				string[] minMaxPriceErrors =
					GetPriceOrPercentageCmdLine(cmdSplit, 3, "minimum", lastTrade.Price, 5, 1, out minPrice, out minPricePercentage);
				if (minMaxPriceErrors != null) return minMaxPriceErrors;

				//sidtodo what if the order book is already running?
				//sidtodo parameterise how many decimal places
				FAmountInTopList fAmountInTopList = (topBid, topAsk) => Decimal.Round(amountToSpend, 0) + prodInfo.smallestVolumeDivision;

				WatchOrderBookRes wobRes = OrderBook.WatchOrderBook(cbClient, prod, HandleExceptions, fAmountInTopList,
					null);

				Func<decimal> fAmountToSpend = () => amountToSpend;
				Func<OrderBookLevel2, decimal> fBestPrice = (ob) => ob.asks.ElementAt(0).Key;

				Func<OrderBookLevel2, decimal, decimal> fGetOurPrice =
					LimitTaskGetOurPrice(numUnderCutUnits, prodInfo, fBestPrice, OrderSide.Sell, minPrice, minPricePercentage);

				var sync = new LimitTaskSynchronisation();

				Action fOutputSuccess = () =>
				{
					var msg = $"Sold {sync.currentAmountToTrade} {prodInfo.spokenName} at " +
						$"{prodInfo.fSpeakPrice(sync.currentPrice)}";
					EventOutput(new EventOutput[] { new EventOutput(msg, null) });
				};

				//sidtodo what if hte order book crashes and we already have a task outstanding?!?!?
				//sidtodo restart - don't restart the task??!

				var limitTaskCancelTS = new CancellationTokenSource();

#pragma warning disable 4014
				var limitTask = Task.Run(async () => await LimitTask(fAmountToSpend, wobRes, EventOutput, prodInfo, cbClient,
					  fGetOurPrice, OrderSide.Sell, fOutputSuccess, fBestPrice, sync, limitTaskCancelTS.Token),
					limitTaskCancelTS.Token);
#pragma warning restore 4014

				Action fCancel = async () =>
				{
					using (await sync.theLock.LockAsync(Timeout.InfiniteTimeSpan))
					{
						if (sync.currentOrderId != null)
						{
							Tuple<bool, bool> cancelRes;
							while ((cancelRes = await CancelOrderOneAttempt(sync.currentOrderId, cbClient)).Item2)
							{
							}

							limitTaskCancelTS.Cancel();
							wobRes.Stop(); //sidtodo - restart the other threads again - polling data e.t.c.

							if (!cancelRes.Item1)
							{
								// Sold ...
								fOutputSuccess();
							}
							else
							{
								//sidtodo output cancelled???
							}
						}
					};
				};

				inProgressCmd.Ref = new InProgressCommand();
				inProgressCmd.Ref.fCancel = fCancel;
			}

			return new string[] { "In progress..." };
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
				bool isInvalidOrderId = (e.Message.ToLower().IndexOf("invalid") >= 0);
				if (isInvalidOrderId)
				{
					return new Tuple<bool,bool>(false,false);
				}

				error = true;
			}

			return new Tuple<bool, bool>(!error, error);
		}

		////SIDTODO HERE: STOP GETTING THE TRADE DATA WHEN DOING A BUY/SELL.
		async static Task LimitTask(Func<decimal> fAmountToTrade, WatchOrderBookRes wobRes,
			EventOutputter EventOutput, ProductInfo prodInfo, CoinbaseProClient cbClient,
			Func<OrderBookLevel2, decimal, decimal> fGetOurPrice, OrderSide orderSide,
			Action fOnSuccess, Func<OrderBookLevel2, decimal> fBestPrice,
			LimitTaskSynchronisation synchronisation,CancellationToken ct)
		{

			await Task.Delay(1000); // Give the order book enough time to kick in

			decimal ourCurrentPrice = 0;

			do
			{

				var ob = wobRes.GetOrderBook();
				bool setThePrice = false;

				DateTime updatePriceTime = DateTime.Now;

				bool errorSending = false;

				if (ob != null)
				{

					decimal newPrice = fGetOurPrice(ob, ourCurrentPrice);

					bool cancelThePrice = (ourCurrentPrice != 0 && newPrice != ourCurrentPrice);
					setThePrice = (ourCurrentPrice == 0 || newPrice != ourCurrentPrice);

					if (setThePrice || cancelThePrice)
					{
						using (await synchronisation.theLock.LockAsync(Timeout.InfiniteTimeSpan))
						{
							if (ct.IsCancellationRequested) return;

							//sidtodo check that we've not been told to stop
							if (cancelThePrice)
							{

								var cancelRes=await CancelOrderOneAttempt(synchronisation.currentOrderId, cbClient);
								bool orderHasBeenSold = (!cancelRes.Item1 && !cancelRes.Item2);
								if (orderHasBeenSold)
								{
									//sidtodo we're finished = done.
									//sidtodo clear the in progress task???
									wobRes.Stop();
									synchronisation.currentOrderId = null;
									fOnSuccess();

									//sidtodo cancel the in progress task.
									return;
								}

								errorSending = cancelRes.Item2;
								if(!errorSending) synchronisation.currentOrderId = null;
							}

							if (ct.IsCancellationRequested) return;

							if (setThePrice && !errorSending)
							{
								decimal currentAmountToTrade = fAmountToTrade();
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
								catch (Exception e)
								{
									errorSending = true;
								}

							}

							if (ct.IsCancellationRequested) return;
						}
					}

				}

				int sleepMs = 100;
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

		// BUYL [funds] [slippage]
		//sidtodo unfinished
		private static async Task<IEnumerable<string>> BuyLimit(string[] cmdSplit,
			Action<Exception> HandleExceptions, Func<ProductStatsDictionary> getProdStats,
			Func<ProductType> getActiveProduct, CoinbaseProClient cbClient,
			EventOutputter EventOutput, LockedByRef<InProgressCommand> inProgressCmd)
		{

			var prod = getActiveProduct();
			var prodInfo = Products.productInfo[prod];
			//await cbClient.OrdersService.PlaceLimitOrderAsync(OrderSide.Buy, prod, 1.0M, 22.80M, TimeInForce.Gtc, true);

			var sourceCurrencyId= Currency.CurrencyId(prodInfo.sourceCurrency);

			var sourceAccount = await cbClient.AccountsService.GetAccountByIdAsync(sourceCurrencyId);

			decimal amountToSpend;
			if (cmdSplit.Length >=2 && !StringCompareNoCase(cmdSplit[1],"all"))
			{
				if (!Decimal.TryParse(cmdSplit[1], out amountToSpend) || amountToSpend<=0)
				{
					return new string[] { $"Invalid amount: {cmdSplit[1]}." };
				}

				amountToSpend = Decimal.Round(amountToSpend, prodInfo.priceNumDecimalPlaces);

				if (amountToSpend > sourceAccount.Available)
				{
					return new string[] { $"{prodInfo.fSpeakPrice(amountToSpend)} is more than is available "+
						$"({prodInfo.fSpeakPrice(sourceAccount.Available)})." };
				}
			}
			else
			{
				amountToSpend = sourceAccount.Available;
			}

			decimal slippage = 0.005M; // Default to half a percent in slippage
			if (cmdSplit.Length == 3)
			{
				if (!Decimal.TryParse(cmdSplit[2], out slippage) || slippage <= 0 || slippage>0.05M)
				{
					return new string[] { $"Invalid slippage: {cmdSplit[2]}." };
				}
			}

			//sidtodo what if the order book is already running?
			//sidtodo parameterise how many decimal places
			FAmountInTopList fAmountInTopList = (topBid, topAsk) => Decimal.Round(amountToSpend / topBid,5)+prodInfo.smallestVolumeDivision;

			WatchOrderBookRes res=OrderBook.WatchOrderBook(cbClient, prod, HandleExceptions, fAmountInTopList,
				null);

			return new string[] { "In progress..." };
		}

		//sidtodo slippage
		//private static Action<OrderBookLevel2> BuyLimitCallback(decimal amountToSpend,
		//	EventOutputter EventOutput, ProductInfo prodInfo)
		//{
		//	//sidtodo if the cancel fails then it's been filled and tehrefore stop.

		//	//how to lock??

		//	// every 1000MS, cancel existing, then update price???

		//	// time in force: 1000MS???

		//	// rather than have callback, should we just poll every 1000 MS??

		//	return (state) =>
		//	{
		//		decimal topBid = state.bids.ElementAt(state.bids.Count - 1).Key;

		//		EventOutput(new EventOutput[] {
		//			new EventOutput($"Top bid is {topBid}, buy at {topBid + prodInfo.smallestPriceDivision}",null)});
		//	};
		//}

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
