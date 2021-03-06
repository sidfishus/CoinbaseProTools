using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoinbaseProToolsForm.Library;
using CoinbasePro;
using Types = CoinbasePro.Shared.Types;
using CoinbasePro.Services.Products.Models;
using CoinbasePro.WebSocket.Types;
using CoinbasePro.Services.Products;
using CoinbasePro.Services.Products.Types;
using CoinbasePro.WebSocket.Models.Response;
using PriceList = System.Collections.Generic.SortedDictionary<decimal, decimal>;
using EventOutputter = System.Action<System.Collections.Generic.IEnumerable<System.Tuple<string, System.DateTimeOffset?>>>;
using EventOutput = System.Tuple<string, System.DateTimeOffset?>;
using ExceptionFileWriter = System.Action<string>;
using ExceptionUIWriter = System.Action<string>;
using OrderBookRange = System.Tuple<string, System.DateTime>;
using ProductType = CoinbasePro.Shared.Types.ProductType;
using FAmountInTopList = System.Func<decimal /* Top bid */, decimal /* Top ask */, decimal /* Num coins */>;
using WatchOrderBookCallback = System.Action<CoinbaseProToolsForm.OrderBookLevel2>;
using System.Threading;

namespace CoinbaseProToolsForm
{
	enum eOrderBookType
	{
		obtUnknown=0,
		obtLevel2=1,
	};

	public struct WatchOrderBookRes
	{
		public Func<Task> Stop;
		public Func<OrderBookLevel2> GetOrderBook;
	}

	public class OrderBookLevel2
	{
		public bool initialised;
		public PriceList asks;
		public PriceList bids;
		public PriceList topAsks;
		public PriceList topBids;
		//public List<OrderBookRange> topBidsAsks = null;
		public readonly object orderBookLock = new object(); //sidtodo right state?

		public OrderBookLevel2 DeepCopy()
		{
			if (!initialised) return null;

			var copy = new OrderBookLevel2();
			copy.asks = new PriceList(this.asks);
			copy.bids = new PriceList(this.bids);
			copy.topAsks= new PriceList(this.topAsks);
			copy.topBids = new PriceList(this.topBids);
			//copy.topBidsAsks = new List<OrderBookRange>(this.topBidsAsks);
			return copy;
		}
	}

	public static class OrderBook
	{
		public static IEnumerable<string> OrderBookCmd(CoinbaseProClient cbClient, string[] param,
			EventOutputter EventOutput, ExceptionUIWriter exceptionUIWriter,
			ExceptionFileWriter exceptionFileWriter)
		{
			if (param.Length == 1)
			{
				return new string[] { "Not enough parameters." };
			}

			//if (StringCompareNoCase(param[1], "INIT"))
			//{
			//	var type = eOrderBookType.obtLevel2;

			//	if (param.Length == 3)
			//	{
			//		if (StringCompareNoCase(param[2], "L2"))
			//		{
			//		}
			//		else
			//		{
			//			return new string[] { $"{param[2]} unknown. Try L2" };
			//		}
			//	}

			//	switch (type)
			//	{
			//		case eOrderBookType.obtLevel2:
			//			return InitLevel2(getSetState, cbClient, EventOutput,
			//				exceptionUIWriter, exceptionFileWriter);
			//	}
			//}
			//else if (StringCompareNoCase(param[1], "STOP"))
			//{
			//	StopOrderBook(cbClient, getSetState);
			//	return null;
			//}
			//else if (StringCompareNoCase(param[1], "PRINT"))
			//{
			//	return PrintOrderBook(cbClient, getSetState);
			//}
			//else if (StringCompareNoCase(param[1], "PRINTHISTORY"))
			//{
			//	return PrintOrderBookHistory(cbClient, getSetState);
			//}

			return new string[] { $"{param[1]} unknown." };
		}

		//private static IEnumerable<string> PrintOrderBookHistory(CoinbaseProClient cbClient, GetSetOrderBookState getSetState)
		//{
		//	var curState = getSetState.Item1();

		//	//TODO this assumes level 2.
		//	OrderBookLevel2 orderBookCopy;

		//	lock (curState.OrderBookLock)
		//	{
		//		orderBookCopy = curState.level2.DeepCopy();
		//	}

		//	var outputList = new List<string>();
		//	for (int i = 0; i < orderBookCopy.topBidsAsks.Count; ++i)
		//	{
		//		var kvp = orderBookCopy.topBidsAsks[i];
		//		//outputList.Add(new EventOutput(kvp.Item1, kvp.Item2));
		//		//outputList.Add(new EventOutput(kvp.Item1, kvp.Item2));
		//		outputList.Add($"{kvp.Item2.ToString("HH:mm:ss")} {kvp.Item1}");
		//	}

		//	return outputList;
		//}

		//private static IEnumerable<string> PrintOrderBook(CoinbaseProClient cbClient, GetSetOrderBookState getSetState)
		//{
		//	var curState = getSetState.Item1();

		//	//TODO this assumes level 2.
		//	OrderBookLevel2 orderBookCopy;

		//	lock (curState.OrderBookLock)
		//	{
		//		orderBookCopy = curState.level2.DeepCopy();
		//	}

		//	IList<string> output = new List<string>();

		//	PrintLevel2Prices(orderBookCopy.asks, output, true);
		//	output.Add(string.Empty);

		//	PrintLevel2Prices(orderBookCopy.bids, output, false);

		//	return output;
		//}

		private static void PrintLevel2Prices(PriceList priceList, IList<string> output, bool isAsks)
		{

			// Reverse - highest first.
			foreach (var pricePair in priceList.Reverse())
			{
				output.Add($"{pricePair.Key} {pricePair.Value}");
			}
		}

		//private static void StopOrderBook(CoinbaseProClient cbClient, GetSetOrderBookState getSetState)
		//{
		//	var curState = getSetState.Item1();
		//	if (curState == null)
		//	{
		//	}
		//	else
		//	{
		//		getSetState.Item2(null);
		//		cbClient.WebSocket.Stop();
		//	}
		//}

		//private static IEnumerable<string> InitLevel2(GetSetOrderBookState getSetState, CoinbaseProClient cbClient,
		//	EventOutputter EventOutput,
		//	ExceptionUIWriter exceptionUIWriter, ExceptionFileWriter exceptionFileWriter)
		//{
		//	var curState = getSetState.Item1();
		//	if (curState != null && curState.level2 != null)
		//	{
		//		return new string[] { $"Order book is already initialised. Stop? OB STOP" };
		//	}

		//	if (curState == null)
		//	{
		//		curState = new OrderBookState();
		//		getSetState.Item2(curState);
		//	}

		//	curState.level2 = new OrderBookLevel2();
		//	curState.level2.asks = new PriceList();
		//	curState.level2.bids = new PriceList();
		//	curState.level2.topAsks = new PriceList();
		//	curState.level2.topBids = new PriceList();
		//	//if (curState.level2.topBidsAsks != null)
		//	//{
		//	//	curState.level2.topBidsAsks.Clear();
		//	//}
		//	//curState.level2.topBidsAsks = new List<OrderBookRange>();

		//	var webSocket = cbClient.WebSocket;
		//	//sidtodo other websocket events.
		//	//sidtodo set the channel types
		//	//var channels = new List<ChannelType>() { ChannelType.Ticker }; // When not providing any channels, the socket will subscribe to all channels
		//	//sidtodo hardcoded to link
		//	webSocket.Start(new ProductType [] { ProductType.LinkGbp }.ToList());
		//	//webSocket.OnLevel2UpdateReceived += (sender, args) =>
		//	//	WebSocket_OnLevel2UpdateReceived(args, getSetState.Item1, EventOutput, exceptionUIWriter,
		//	//		exceptionFileWriter, getSetState, cbClient);
		//	//webSocket.OnSnapShotReceived += (sender, args) =>
		//		//WebSocket_OnSnapShotReceived(args, getSetState.Item1, EventOutput,
		//		//	exceptionUIWriter, exceptionFileWriter, getSetState, cbClient);

		//	//webSocket.OnWebSocketError += (sender, args) => WebSocket_OnWebSocketError(sender,args, exceptionFileWriter, exceptionUIWriter);

		//	return null;
		//}

		private static void WebSocket_OnSnapShotReceived(WebfeedEventArgs<Snapshot> args, OrderBookLevel2 state,
			Action restartOrderBook)
		{

			try
			{

				lock (state.orderBookLock)
				{
					foreach (var ask in args.LastOrder.Asks)
					{
						if (ask.Length == 2)
						{
							// <Price> <num> pairs
							state.asks.Add(ask[0], ask[1]);

							//EventOutput($"{ask[0]} {ask[1]} sell"); //sidtodo remove
						}
					}

					foreach (var bid in args.LastOrder.Bids)
					{
						if (bid.Length == 2)
						{
							// <Price> <num> pairs
							state.bids.Add(bid[0], bid[1]);
							//EventOutput($"{bid[0]} {bid[1]} sell"); //sidtodo remove
						}
					}
				}
			}
#pragma warning disable 168
			catch(Exception e)
#pragma warning restore 168
			{
				restartOrderBook();
			}
		}

		private static void InitialiseTopOrderBookEntries(PriceList topPrices,
			IEnumerable<KeyValuePair<decimal,decimal>> fullPrices,
			decimal numCoinsInTop)
		{
			decimal accumulatedCoins = 0;
			foreach (var priceTuple in fullPrices)
			{
				//outputList.Add(new EventOutput($"{priceTuple.Key} {priceTuple.Value}", null));
				topPrices.Add(priceTuple.Key,priceTuple.Value);
				accumulatedCoins += priceTuple.Value;

				if (accumulatedCoins >= numCoinsInTop)
				{
					break;
				}
			}
		}

		// Assumes the order book is already locked
		public static WatchOrderBookRes WatchOrderBook(CoinbaseProClient cbClient, ProductType product,
			Action<Exception> handleExceptions,
			FAmountInTopList fAmountInTopList, WatchOrderBookCallback watchOrderBookCallbackOptional,
			WebSocketState webSocketState)
		{
			var restartLock = new TimedLock();
			return WatchOrderBook(cbClient, product, handleExceptions, fAmountInTopList, watchOrderBookCallbackOptional,
				webSocketState, restartLock);
		}

		// Assumes the order book is already locked
		private static WatchOrderBookRes WatchOrderBook(CoinbaseProClient cbClient, ProductType product,
			Action<Exception> handleExceptions,
			FAmountInTopList fAmountInTopList /* not actually used */, WatchOrderBookCallback watchOrderBookCallbackOptional,
			WebSocketState webSocketState, TimedLock restartLock)
		{
			var webSocket = cbClient.WebSocket;
			bool stop = false;

			Action restartOrderBook = async() =>
			{
				if (!stop)
				{
					using (await webSocketState.theLock.LockAsync(Timeout.InfiniteTimeSpan))
					using (await restartLock.LockAsync(Timeout.InfiniteTimeSpan))
					{
						if (webSocket.State != WebSocket4Net.WebSocketState.Connecting)
						{
							WatchOrderBook(cbClient, product, handleExceptions, fAmountInTopList, watchOrderBookCallbackOptional,
								webSocketState);

							await Task.Delay(1000);
						}
					}
				}
			};

			WebSocket.Stop(webSocketState, cbClient);

			var state = new OrderBookLevel2();
			state.initialised = false;
			state.asks = new PriceList();
			state.bids = new PriceList();
			state.topAsks = new PriceList();
			state.topBids = new PriceList();
			//state.topBidsAsks = new List<OrderBookRange>();

			WatchOrderBookRes res = new WatchOrderBookRes();
			res.Stop = async () =>
			{
				stop = true;
				using (await webSocketState.theLock.LockAsync(Timeout.InfiniteTimeSpan))
				{
					WebSocket.Stop(webSocketState, cbClient);
				}
			};

			res.GetOrderBook = () =>
			{
				OrderBookLevel2 rv;
				lock(state.orderBookLock)
				{
					rv = state.DeepCopy();
				}
				return rv;
			};

			webSocketState.onWebSocketError= (sender, e) => WebSocket_OnWebSocketError(e, handleExceptions);
			webSocket.OnWebSocketError += webSocketState.onWebSocketError;

			webSocketState.onLevel2Received = (sender, args) =>
				WebSocket_OnLevel2UpdateReceived(args, state, restartOrderBook, null,
				watchOrderBookCallbackOptional);
			webSocket.OnLevel2UpdateReceived += webSocketState.onLevel2Received;

			webSocketState.onSnapshotReceived = (sender, args) => WebSocket_OnSnapShotReceived(args, state, restartOrderBook);
			webSocket.OnSnapShotReceived += webSocketState.onSnapshotReceived;

			var channels = new List<ChannelType>() { ChannelType.Level2 };
			webSocket.Start(new ProductType[] { product }.ToList());

			return res;
		}

		private static void WebSocket_OnWebSocketError(WebfeedEventArgs<SuperSocket.ClientEngine.ErrorEventArgs> e,
			Action<Exception> handleExceptions)
		{
			handleExceptions(e.LastOrder.Exception);
		}

		private static void WebSocket_OnLevel2UpdateReceived(WebfeedEventArgs<Level2> args, OrderBookLevel2 state,
			Action restartOrderBook, Func<decimal> fNumCoinsToHoldInTopList,
			WatchOrderBookCallback watchOrderBookCallbackOptional)
		{

			OrderBookLevel2 stateCopyForCallback=null;

			try
			{

				lock (state.orderBookLock)
				{
					state.initialised = true;

					// Initialise the top 5 bids and asks.
					//if (state.asks.Count != 0 || state.asks.Count!=0)
					//{
					//	// Iterate the asks (sells) descending (lowest price first)
					//	var outputList = new List<EventOutput>();
					//	InitialiseTopOrderBookEntries(state.topAsks, state.asks);
					//	InitialiseTopOrderBookEntries(state.topBids, state.bids.Reverse());
					//}

					// Update the incore order book
					for (int i = 0; i < args.LastOrder.Changes.Count(); ++i)
					{
						// buy/sell <price> <num>
						var change = args.LastOrder.Changes[i];

						if (change.Length == 3)
						{
							PriceList priceList;
							PriceList topPriceList;
							bool priceInReverse;

							if (StringCompareNoCase(change[0], "sell"))
							{
								priceList = state.asks;
								topPriceList = state.topAsks;
								priceInReverse = false;

								if (state.topBids.Count == 0 && fNumCoinsToHoldInTopList!=null)
								{
									InitialiseTopOrderBookEntries(state.topBids, state.bids, fNumCoinsToHoldInTopList());
								}
							}
							else if (StringCompareNoCase(change[0], "buy"))
							{
								priceList = state.bids;
								topPriceList = state.topBids;
								priceInReverse = true;

								if (state.topAsks.Count == 0 && fNumCoinsToHoldInTopList != null)
								{
									InitialiseTopOrderBookEntries(state.topAsks, state.asks, fNumCoinsToHoldInTopList());
								}
							}
							else return;

							decimal price;
							if (!decimal.TryParse(change[1], out price))
							{
								return;
							}

							//sidtodo test this with other currencies
							bool isDelete = (change[2] == "0.00");
							if (isDelete)
							{
								if (!priceList.Remove(price))
								{
									//sidtodo
									//EventOutput(new EventOutput[] { new EventOutput(
									//		$"****** Price {price} not exist in dictionary. told to remove. {change[0]}. {args.LastOrder.Changes.Count()}", null)});
									restartOrderBook();
									return;
								}
							}
							else
							{

								decimal number;
								if (!decimal.TryParse(change[2], out number))
								{
									//sidtodo
									//EventOutput(new EventOutput[] { new EventOutput($"Couldn't parse number: ${change[2]}", null) }); //sidtodo remove
									return;
								}

								priceList[price] = number;
							}

							// Delete the top price list and repopulate it
							if (fNumCoinsToHoldInTopList != null)
							{
								topPriceList.Clear();
								InitialiseTopOrderBookEntries(topPriceList, ((priceInReverse) ? priceList.Reverse() : priceList),
									fNumCoinsToHoldInTopList());
							}

							//EventOutput($"{change[0]} {change[1]} {change[2]} {args.LastOrder.Time}");
						}
					}

					if(watchOrderBookCallbackOptional!=null) stateCopyForCallback = state.DeepCopy();

					// Output the top bids and asks
					//if (state.topBids.Count > 0 && state.topAsks.Count > 0)
					//{
					//	string curTopBidAsksStr = $"{state.topBids.First().Key}-" +
					//		$"{state.topBids.Last().Key} {state.topAsks.First().Key}-{state.topAsks.Last().Key}";

					//	if (state.topBidsAsks.Count == 0 ||
					//		!Library.StringCompareNoCase(state.topBidsAsks[state.topBidsAsks.Count - 1].Item1, curTopBidAsksStr))
					//	{
					//		state.topBidsAsks.Add(new OrderBookRange(curTopBidAsksStr, DateTime.Now));
					//	}
					//}

					//for (int i = 0; i < args.LastOrder.Changes.Count(); ++i)
					//{
					//	// buy/sell <price> <num>
					//	var change = args.LastOrder.Changes[i];

					//	if (change.Length == 3)
					//	{
					//		PriceList priceList;

					//		if (StringCompareNoCase(change[0], "sell"))
					//		{
					//			priceList = curState.level2.asks;
					//		}
					//		else if (StringCompareNoCase(change[0], "buy"))
					//		{
					//			priceList = curState.level2.bids;
					//		}
					//		else return;

					//		decimal price;
					//		if (!decimal.TryParse(change[1], out price))
					//		{
					//			return;
					//		}

					//		bool isDelete = (change[2] == "0.00");
					//		if (isDelete)
					//		{
					//			if (!priceList.Remove(price))
					//			{
					//				EventOutput(new EventOutput[] { new EventOutput(
					//					$"****** Price {price} not exist in dictionary. told to remove. {change[0]}. {args.LastOrder.Changes.Count()}", null)});
					//			}
					//		}
					//		else
					//		{

					//			decimal number;
					//			if (!decimal.TryParse(change[2], out number))
					//			{
					//				EventOutput(new EventOutput[] { new EventOutput($"Couldn't parse number: ${change[2]}",null) }); //sidtodo remove
					//				return;
					//			}

					//			priceList[price] = number;
					//		}

					//		//EventOutput($"{change[0]} {change[1]} {change[2]} {args.LastOrder.Time}");
					//	}
					//}

					//decimal highestBid = curState.level2.bids.Last().Key;
					//decimal lowestAsk = curState.level2.asks.First().Key;

					//EventOutput($"Diff = {lowestAsk-highestBid}");
					//decimal diffRatio = Decimal.Round((decimal)100.0 - ((highestBid / lowestAsk)*(decimal)100.0),2);
					//decimal diffRatio = Decimal.Round((decimal)100.0 - ((highestBid / lowestAsk) * (decimal)100.0), 2);
					////EventOutput($"Ratio = {diffRatio}");

					//if (diffRatio >= (decimal)1)
					//{
					//	//EventOutput($"Diff = {Decimal.Round(lowestAsk - highestBid,2)}, Ratio = {diffRatio}%{Environment.NewLine} "+
					//	//	$"{DateTime.Now.ToString()}\t{Decimal.Round(lowestAsk, 5)} "+
					//	//	$"{Environment.NewLine}\t{Decimal.Round(highestBid, 5)}");
					//}
				}

				//var output = new List<EventOutput>();

				//for (int i = 0; i < args.LastOrder.Changes.Count; ++i)
				//{
				//	var change = args.LastOrder.Changes[i];
				//	output.Add(new EventOutput($"{change[0]} {change[1]} {change[2]}", null));
				//}

				//EventOutput(output);
			}
#pragma warning disable 168
			catch (Exception e)
#pragma warning restore 168
			{
				restartOrderBook();
				return;
			}

			if (stateCopyForCallback != null)
			{
				watchOrderBookCallbackOptional(stateCopyForCallback);
			}
		}
	}
}
