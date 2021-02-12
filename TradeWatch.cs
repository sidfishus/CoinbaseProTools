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

namespace CoinbaseProToolsForm
{

	public static class TradeWatch
	{

		public static async Task<IEnumerable<string>> CmdLine(CoinbaseProClient cbClient,
			string[] cmdSplit, EventOutputter EventOutput,
			Func<ProductStatsDictionary> getProdStats,
			Action<Exception> HandleExceptions, TradeHistoryState tradeHistoryState,
			Dictionary<ProductType,NewTradesTriggerList> newTradesTriggers,
			Func<ProductType> getActiveProduct, Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers)
		{

			if (StringCompareNoCase(cmdSplit[1], "TEST1"))
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
				return ShowLevel(cmdSplit, tradeHistoryState,getProdStats, true, getActiveProduct());
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWTRADES"))
			{
				return await ShowTrades(cmdSplit, tradeHistoryState, HandleExceptions,
					cbClient, null, getActiveProduct);
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWSELLS"))
			{
				return await ShowTrades(cmdSplit, tradeHistoryState, HandleExceptions,
					cbClient, (price, size)=> size<0, getActiveProduct);
			}
			else if (StringCompareNoCase(cmdSplit[1], "SHOWBUYS"))
			{
				return await ShowTrades(cmdSplit, tradeHistoryState, HandleExceptions,
					cbClient, (price, size) => size > 0, getActiveProduct);
			}

			return null;
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
