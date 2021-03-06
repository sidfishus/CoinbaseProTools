using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using TimeAndPrice = System.Tuple<System.DateTime, decimal>;

namespace CoinbaseProToolsForm
{
	public class CandleState
	{
		public int durationSecs;
		public decimal Low;
		public decimal High;
		public DateTime startTime;
		public DateTime endTime;
		public decimal Open;
		public decimal Close;

		public List<TimeAndPrice> priceList=new List<TimeAndPrice>();

		public bool initialised;

		public CandleState(int durationSecs)
		{
			initialised = false;
			this.durationSecs = durationSecs;
		}
	}

	public static class Candle
	{

		public static void UpdateLowAndHighPrice(
			CandleState candle, decimal price)
		{
			if (candle.Low == 0 || price < candle.Low)
			{
				candle.Low = price;
			}
			if (price > candle.High)
			{
				candle.High = price;
			}
		}

		public static void AddNewPrice(CandleState candle,
			decimal price, DateTime ts)
		{
			if (!candle.initialised)
			{
				candle.startTime = ts;
				candle.initialised = true;
				candle.Open = price;
			}

			candle.endTime = ts;

			candle.priceList.Add(new TimeAndPrice(ts, price));

			if (HaveFullDataset(candle))
			{
				// Update the start timestamp
				candle.startTime = candle.endTime.AddSeconds(-candle.durationSecs);

				// Remove any prices which are out of the range
				for (int i = candle.priceList.Count()-2 /* Subtle -2 (not -1) */; i >= 0; --i)
				{
					var priceAndTime = candle.priceList[i];
					if (priceAndTime.Item1 < candle.startTime)
					{
						candle.Open = candle.priceList[i].Item2;

						// Remove this price and the ones below it
						candle.priceList.RemoveRange(0, i+1);
						break;
					}
				}

				// Update the low and high prices
				candle.Low = 0;
				candle.High = 0;
				for (int i = candle.priceList.Count()-1; i >= 0; --i)
				{
					UpdateLowAndHighPrice(candle, candle.priceList[i].Item2);
				}
			}
			else
			{
				UpdateLowAndHighPrice(candle, price);
			}

			candle.Close = price;
		}

		public static bool HaveFullDataset(CandleState candle)
		{
			return (candle.endTime-candle.startTime).TotalSeconds>=candle.durationSecs;
		}

		public static decimal PriceDropPercentage(CandleState candle)
		{
			// Price decreased
			if (candle.Open > candle.Close)
			{
				decimal priceDiff = Math.Abs(candle.High - candle.Low);
				decimal priceDiffPercentage = priceDiff / candle.High;
				return priceDiffPercentage;
			}

			return 0;
		}
	}
}
