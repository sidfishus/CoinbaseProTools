using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoinbasePro;
using CoinbasePro.Services.Orders.Types;
using ProductType = CoinbasePro.Shared.Types.ProductType;

namespace CoinbaseProToolsForm
{
	public static class Orders
	{
		public static async Task<bool> BuyMarket(CoinbaseProClient cbClient,
			decimal amountToSpend,ProductType product,Action<Exception> fHandleExceptions,
			bool round=true)
		{
			if (round == true)
			{
				var prodInfo = Products.productInfo[product];
				amountToSpend = Misc.TruncateRound(amountToSpend, prodInfo.priceNumDecimalPlaces);
			}

			try
			{
				await cbClient.OrdersService.PlaceMarketOrderAsync(OrderSide.Buy, product, amountToSpend, MarketOrderAmountType.Funds);
				return true;
			}
			catch (Exception e)
			{
				if (Misc.IsNoInternetException(e)) return false;

				fHandleExceptions(e);

				throw e;
			}
		}

		public static async Task<bool> SellMarket(CoinbaseProClient cbClient,
			decimal amountToSpend, ProductType product, Action<Exception> fHandleExceptions,
			bool round = true)
		{
			if (round == true)
			{
				var prodInfo = Products.productInfo[product];
				amountToSpend = Misc.TruncateRound(amountToSpend, prodInfo.volNumDecimalPlaces);
			}

			try
			{
				await cbClient.OrdersService.PlaceMarketOrderAsync(OrderSide.Sell, product, amountToSpend, MarketOrderAmountType.Size);
				return true;
			}
			catch (Exception e)
			{
				if (Misc.IsNoInternetException(e)) return false;

				fHandleExceptions(e);

				throw e;
			}
		}

		private static bool IsInvalidBuySellException(string msg)
		{
			//funds is too small
			//insufficient funds
			// size is too accurate
			// funds is too accurate

			return
				(
					(msg.IndexOf("funds")>=0 || msg.IndexOf("size")>=0) &&
					(msg.IndexOf("small")>=0 || msg.IndexOf("large") >= 0 || msg.IndexOf("insufficient")>=0)
				) || msg.IndexOf("too accurate")>=0;
		}
	}
}
