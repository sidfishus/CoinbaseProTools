using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoinbasePro;
using CoinbasePro.Services.Products.Models;
using CBPagedTradeList = System.Collections.Generic.IList<System.Collections.Generic.IList<CoinbasePro.Services.Products.Models.ProductTrade>>;
using EventOutputter = System.Action<System.Collections.Generic.IEnumerable<System.Tuple<string, System.DateTimeOffset?>>>;
using EventOutput = System.Tuple<string, System.DateTimeOffset?>;
using SummaryLevelUpdateCallback = System.Action<System.Collections.Generic.Dictionary<int /* Summary period in units */, CoinbaseProToolsForm.TradeSummaryState>>;
using ProductStatsDictionary = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType, CoinbasePro.Services.Products.Types.ProductStats>;
using ProductStats = CoinbasePro.Services.Products.Types.ProductStats;
using NewTradesCallback = System.Action<System.Collections.Generic.Dictionary<int /* Summary period in units */,
	CoinbaseProToolsForm.TradeSummaryState>,
	System.Collections.Generic.List<CoinbasePro.Services.Products.Models.ProductTrade>>;
using ProductType = CoinbasePro.Shared.Types.ProductType;

namespace CoinbaseProToolsForm
{
	public static class PollForUpdates
	{
		public static async Task PollForUpdatesAsync(CoinbaseProClient cbClient,
			Action<Exception> handleException, TradeHistoryState tradeHistory,
			EventOutputter Output, Func<ProductStatsDictionary> getProductStats,
			SummaryLevelUpdateCallback summaryLevelUpdateCallback,
			NewTradesCallback newTradesCallback, ProductType productType,
			Func<bool> outputTradeSummary, bool complainNoInternet,
			Func<bool> fEnabled)
		{

			DateTimeOffset lastTsComplainedNoInternet = DateTime.Now.AddDays(-1);

			while (true)
			{

				if (!fEnabled())
				{
					await Task.Delay(5000);
					continue;
				}

				for (int amountOfPagesToLoad = 1; ;)
				{
					CBPagedTradeList trades = null;

					try
					{
						trades = await cbClient.ProductsService.GetTradesAsync(productType, ((amountOfPagesToLoad==1)?50:100), amountOfPagesToLoad);
					}
					catch (Exception e)
					{
						handleException?.Invoke(e);

						if (complainNoInternet)
						{
							var now = DateTime.Now;
							if ((now - lastTsComplainedNoInternet).TotalSeconds >= 30)
							{
								lastTsComplainedNoInternet = now;
								Library.AsyncSpeak("No internet.");
							}
						}
					}

					if (trades != null)
					{
						DateTimeOffset tradeLoadedTime = DateTime.Now;

						var prodstatsDictionary = getProductStats();
						ProductStats prodStats = null;
						if (prodstatsDictionary!=null)
						{
							prodstatsDictionary.TryGetValue(productType, out prodStats);
						}

						if (!TradeHistory.ConditionalAddTrades(trades, tradeHistory, ((outputTradeSummary())?Output:null),
							prodStats, summaryLevelUpdateCallback, newTradesCallback, productType))
						{
							// We don't have the latest data, load more next time.
							amountOfPagesToLoad *= 3;
						}
						else
						{
							amountOfPagesToLoad = 1;

							int msSinceLoaded = (int)(DateTime.Now - tradeLoadedTime).TotalMilliseconds;
							if (msSinceLoaded < 5000)
							{
								await Task.Delay(5000-msSinceLoaded);
							}
						}
					}
				}
			}
		}
	}
}
