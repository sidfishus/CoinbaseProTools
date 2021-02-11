using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoinbaseProToolsForm.Library;
using CoinbasePro;
using GetSetOrderBookState = System.Tuple<System.Func<CoinbaseProToolsForm.OrderBookState>, System.Action<CoinbaseProToolsForm.OrderBookState>>;
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

namespace CoinbaseProToolsForm
{
	enum eOrderBookType
	{
		obtUnknown=0,
		obtLevel2=1,
	};

	public class OrderBookState
	{
		public OrderBookLevel2 level2;
		public readonly object OrderBookLock = new object();
	}

	public class OrderBookLevel2
	{
		public PriceList asks;
		public PriceList bids;
		public PriceList topAsks;
		public PriceList topBids;
		public List<OrderBookRange> topBidsAsks = null;

		public OrderBookLevel2 DeepCopy()
		{
			var copy = new OrderBookLevel2();
			copy.asks = new PriceList(this.asks);
			copy.bids = new PriceList(this.bids);
			copy.topAsks= new PriceList(this.topAsks);
			copy.topBids = new PriceList(this.topBids);
			copy.topBidsAsks = new List<OrderBookRange>(this.topBidsAsks);
			return copy;
		}
	}

	public static class OrderBook
	{
		public static IEnumerable<string> OrderBookCmd(CoinbaseProClient cbClient, string[] param,
			GetSetOrderBookState getSetState,
			EventOutputter EventOutput, ExceptionUIWriter exceptionUIWriter, ExceptionFileWriter exceptionFileWriter)
		{
			if (param.Length == 1)
			{
				return new string[] { "Not enough parameters." };
			}

			if (StringCompareNoCase(param[1], "INIT"))
			{
				var type = eOrderBookType.obtLevel2;

				if (param.Length == 3)
				{
					if (StringCompareNoCase(param[2], "L2"))
					{
					}
					else
					{
						return new string[] { $"{param[2]} unknown. Try L2" };
					}
				}

				switch (type)
				{
					case eOrderBookType.obtLevel2:
						return InitLevel2(getSetState, cbClient, EventOutput,
							exceptionUIWriter, exceptionFileWriter);
				}
			}
			else if (StringCompareNoCase(param[1], "STOP"))
			{
				StopOrderBook(cbClient, getSetState);
				return null;
			}
			else if (StringCompareNoCase(param[1], "PRINT"))
			{
				return PrintOrderBook(cbClient, getSetState);
			}
			else if (StringCompareNoCase(param[1], "PRINTHISTORY"))
			{
				return PrintOrderBookHistory(cbClient, getSetState);
			}

			return new string[] { $"{param[1]} unknown." };
		}

		private static IEnumerable<string> PrintOrderBookHistory(CoinbaseProClient cbClient, GetSetOrderBookState getSetState)
		{
			var curState = getSetState.Item1();

			//TODO this assumes level 2.
			OrderBookLevel2 orderBookCopy;

			lock (curState.OrderBookLock)
			{
				orderBookCopy = curState.level2.DeepCopy();
			}

			var outputList = new List<string>();
			for (int i = 0; i < orderBookCopy.topBidsAsks.Count; ++i)
			{
				var kvp = orderBookCopy.topBidsAsks[i];
				//outputList.Add(new EventOutput(kvp.Item1, kvp.Item2));
				//outputList.Add(new EventOutput(kvp.Item1, kvp.Item2));
				outputList.Add($"{kvp.Item2.ToString("HH:mm:ss")} {kvp.Item1}");
			}

			return outputList;
		}

		private static IEnumerable<string> PrintOrderBook(CoinbaseProClient cbClient, GetSetOrderBookState getSetState)
		{
			var curState = getSetState.Item1();

			//TODO this assumes level 2.
			OrderBookLevel2 orderBookCopy;

			lock (curState.OrderBookLock)
			{
				orderBookCopy = curState.level2.DeepCopy();
			}

			IList<string> output = new List<string>();

			PrintLevel2Prices(orderBookCopy.asks, output, true);
			output.Add(string.Empty);

			PrintLevel2Prices(orderBookCopy.bids, output, false);

			return output;
		}

		private static void PrintLevel2Prices(PriceList priceList, IList<string> output, bool isAsks)
		{

			// Reverse - highest first.
			foreach (var pricePair in priceList.Reverse())
			{
				output.Add($"{pricePair.Key} {pricePair.Value}");
			}
		}

		private static void StopOrderBook(CoinbaseProClient cbClient, GetSetOrderBookState getSetState)
		{
			var curState = getSetState.Item1();
			if (curState == null)
			{
			}
			else
			{
				getSetState.Item2(null);
				cbClient.WebSocket.Stop();
			}
		}

		private static IEnumerable<string> InitLevel2(GetSetOrderBookState getSetState, CoinbaseProClient cbClient,
			EventOutputter EventOutput,
			ExceptionUIWriter exceptionUIWriter, ExceptionFileWriter exceptionFileWriter)
		{
			var curState = getSetState.Item1();
			if (curState != null && curState.level2 != null)
			{
				return new string[] { $"Order book is already initialised. Stop? OB STOP" };
			}

			if (curState == null)
			{
				curState = new OrderBookState();
				getSetState.Item2(curState);
			}

			curState.level2 = new OrderBookLevel2();
			curState.level2.asks = new PriceList();
			curState.level2.bids = new PriceList();
			curState.level2.topAsks = new PriceList();
			curState.level2.topBids = new PriceList();
			if (curState.level2.topBidsAsks != null)
			{
				curState.level2.topBidsAsks.Clear();
			}
			curState.level2.topBidsAsks = new List<OrderBookRange>();

			var webSocket = cbClient.WebSocket;
			//sidtodo other websocket events.
			//sidtodo set the channel types
			//var channels = new List<ChannelType>() { ChannelType.Ticker }; // When not providing any channels, the socket will subscribe to all channels
			//sidtodo hardcoded to link
			webSocket.Start(new ProductType [] { ProductType.LinkGbp }.ToList());
			webSocket.OnLevel2UpdateReceived += (sender, args) =>
				WebSocket_OnLevel2UpdateReceived(args, getSetState.Item1, EventOutput, exceptionUIWriter,
					exceptionFileWriter, getSetState, cbClient);
			webSocket.OnSnapShotReceived += (sender, args) =>
				WebSocket_OnSnapShotReceived(args, getSetState.Item1, EventOutput,
					exceptionUIWriter, exceptionFileWriter, getSetState, cbClient);

			webSocket.OnWebSocketError += (sender, args) => WebSocket_OnWebSocketError(sender,args, exceptionFileWriter, exceptionUIWriter);

			return null;
		}

		private static void WebSocket_OnWebSocketError(object sender, WebfeedEventArgs<SuperSocket.ClientEngine.ErrorEventArgs> e,
			ExceptionUIWriter exceptionUIWriter, ExceptionFileWriter exceptionFileWriter)
		{
			var exceptionText = e.LastOrder.Exception.ToString();
			exceptionFileWriter(exceptionText);
			exceptionUIWriter(exceptionText);
		}

		private static void WebSocket_OnSnapShotReceived(WebfeedEventArgs<Snapshot> args, Func<OrderBookState> getState,
			EventOutputter EventOutput, ExceptionUIWriter exceptionUIWriter, ExceptionFileWriter exceptionFileWriter,
			GetSetOrderBookState getSetState, CoinbaseProClient cbClient)
		{

			try
			{
				var curState = getState();
				if (curState == null)
				{
					// Potentially receiving an update after the web socket stopped?
					return;
				}

				lock (curState.OrderBookLock)
				{
					foreach (var ask in args.LastOrder.Asks)
					{
						if (ask.Length == 2)
						{
							// <Price> <num> pairs
							curState.level2.asks.Add(ask[0], ask[1]);

							//EventOutput($"{ask[0]} {ask[1]} sell"); //sidtodo remove
						}
					}

					foreach (var bid in args.LastOrder.Bids)
					{
						if (bid.Length == 2)
						{
							// <Price> <num> pairs
							curState.level2.bids.Add(bid[0], bid[1]);
							//EventOutput($"{bid[0]} {bid[1]} sell"); //sidtodo remove
						}
					}
				}
			}
			catch(Exception e)
			{
				var text = e.ToString();
				exceptionFileWriter(text);
				exceptionUIWriter(text);

				var state = getState();
				if (state != null)
				{
					lock (state.OrderBookLock)
					{
						StopOrderBook(cbClient, getSetState);
						InitLevel2(getSetState, cbClient, EventOutput, exceptionUIWriter, exceptionFileWriter);
					}
				}
			}
		}

		private static void InitialiseTopOrderBookEntries(PriceList topPrices, IEnumerable<KeyValuePair<decimal,decimal>> fullPrices)
		{
			decimal accumulatedCoins = 0;
			foreach (var priceTuple in fullPrices)
			{
				//outputList.Add(new EventOutput($"{priceTuple.Key} {priceTuple.Value}", null));
				topPrices.Add(priceTuple.Key,priceTuple.Value);
				accumulatedCoins += priceTuple.Value;

				if (accumulatedCoins >= 1000)
				{
					break;
				}
			}
		}

		private static void WebSocket_OnLevel2UpdateReceived(WebfeedEventArgs<Level2> args, Func<OrderBookState> getState,
			EventOutputter EventOutput, ExceptionUIWriter exceptionUIWriter, ExceptionFileWriter exceptionFileWriter,
			GetSetOrderBookState getSetState, CoinbaseProClient cbClient)
		{
			try
			{
				var curState = getState();
				if (curState == null)
				{
					// Potentially receiving an update after the web socket stopped?
					return;
				}

				lock (curState.OrderBookLock)
				{

					// Initialise the top 5 bids and asks.
					//if (curState.level2.asks.Count != 0 || curState.level2.asks.Count!=0)
					//{
					//	// Iterate the asks (sells) descending (lowest price first)
					//	var outputList = new List<EventOutput>();
					//	InitialiseTopOrderBookEntries(curState.level2.topAsks, curState.level2.asks);
					//	InitialiseTopOrderBookEntries(curState.level2.topBids, curState.level2.bids.Reverse());
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
								priceList = curState.level2.asks;
								topPriceList = curState.level2.topAsks;
								priceInReverse = false;

								if (curState.level2.topBids.Count == 0)
								{
									InitialiseTopOrderBookEntries(curState.level2.topBids, curState.level2.bids);
								}
							}
							else if (StringCompareNoCase(change[0], "buy"))
							{
								priceList = curState.level2.bids;
								topPriceList = curState.level2.topBids;
								priceInReverse = true;

								if (curState.level2.topAsks.Count == 0)
								{
									InitialiseTopOrderBookEntries(curState.level2.topAsks, curState.level2.asks);
								}
							}
							else return;

							decimal price;
							if (!decimal.TryParse(change[1], out price))
							{
								return;
							}

							bool isDelete = (change[2] == "0.00");
							if (isDelete)
							{
								if (!priceList.Remove(price))
								{
									EventOutput(new EventOutput[] { new EventOutput(
										$"****** Price {price} not exist in dictionary. told to remove. {change[0]}. {args.LastOrder.Changes.Count()}", null)});
								}
							}
							else
							{

								decimal number;
								if (!decimal.TryParse(change[2], out number))
								{
									EventOutput(new EventOutput[] { new EventOutput($"Couldn't parse number: ${change[2]}", null) }); //sidtodo remove
									return;
								}

								priceList[price] = number;
							}

							// Delete the top price list and repopulate it
							topPriceList.Clear();
							InitialiseTopOrderBookEntries(topPriceList, ((priceInReverse) ? priceList.Reverse() : priceList));

							//EventOutput($"{change[0]} {change[1]} {change[2]} {args.LastOrder.Time}");
						}
					}

					// Output the top bids and asks
					if (curState.level2.topBids.Count > 0 && curState.level2.topAsks.Count > 0)
					{
						string curTopBidAsksStr = $"{curState.level2.topBids.First().Key}-" +
							$"{curState.level2.topBids.Last().Key} {curState.level2.topAsks.First().Key}-{curState.level2.topAsks.Last().Key}";

						if (curState.level2.topBidsAsks.Count ==0 ||
							!Library.StringCompareNoCase(curState.level2.topBidsAsks[curState.level2.topBidsAsks.Count-1].Item1,curTopBidAsksStr))
						{
							curState.level2.topBidsAsks.Add(new OrderBookRange(curTopBidAsksStr, DateTime.Now));
						}
					}

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
			catch (Exception e)
			{
				var text = e.ToString();
				exceptionFileWriter(text);
				exceptionUIWriter(text);

				var state = getState();
				if (state != null)
				{
					lock (state.OrderBookLock)
					{
						StopOrderBook(cbClient, getSetState);
						InitLevel2(getSetState, cbClient, EventOutput, exceptionUIWriter, exceptionFileWriter);
					}
				}
			}
		}
	}
}
