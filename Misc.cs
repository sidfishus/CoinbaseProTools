using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoinbasePro;

namespace CoinbaseProToolsForm
{
	public static class Misc
	{
		public static void AddNewDayConditionalUpdateLastTs(IList<string> outputList,
			ref DateTimeOffset lastTs, DateTimeOffset thisTime)
		{
			if (lastTs.Day != thisTime.Day || lastTs.Month!=thisTime.Month || lastTs.Year!=thisTime.Year)
			{
				var nl = Environment.NewLine;
				var conditionalNl = ((outputList.Count == 0) ? "" : nl);
				outputList.Add($"{conditionalNl}{thisTime.ToString("dddd dd MMMM")}{nl}");
			}

			lastTs = thisTime;
		}

		public static string OutputTimestamp(DateTimeOffset ts, bool full=false)
		{
			return ts.ToString(((full)?"HH:mm:ss":"HH:mm"));
		}

		public static string[] GetPriceCmdLine(string[] cmdSplit, int idx,
			string errorMsgDescribePrice,out decimal price)
		{
			var priceStr = cmdSplit[idx];
			decimal.TryParse(priceStr, out price);

			if (price <= 0)
			{
				return new string[] { $"Invalid {errorMsgDescribePrice} price: {price}." };
			}

			return null;
		}

		public static string[] GetPriceOrPercentageCmdLine(string[] cmdSplit, int idx,
			string errorMsgDescribePrice,decimal currentPrice, decimal mustBeWithinPercentageOptional,
			decimal defaultPricePercentage,
			out decimal price, out decimal pricePercentage)
		{
			price = 0;
			pricePercentage = 0;

			if (cmdSplit.Length >= (idx+1))
			{
				var priceStr = cmdSplit[idx];
				char lastChar = priceStr[priceStr.Length - 1];
				if (lastChar == '%')
				{
					priceStr = priceStr.Substring(0, priceStr.Length - 1);
					if (!decimal.TryParse(priceStr, out pricePercentage) ||
						pricePercentage <= 0 ||
						(mustBeWithinPercentageOptional!=0 && pricePercentage > mustBeWithinPercentageOptional))
					{
						return new string[] { $"Invalid {errorMsgDescribePrice} price percentage: {pricePercentage}." };
					}
					pricePercentage /= 100;
				}
				else
				{
					decimal.TryParse(priceStr, out price);
					decimal priceDiffPercentage = 0;
					if (price > 0 && mustBeWithinPercentageOptional!=0)
					{
						decimal priceDiff = Math.Abs(price - currentPrice);
						priceDiffPercentage = (priceDiff / currentPrice)*100;
					}

					if (price <= 0 || priceDiffPercentage> mustBeWithinPercentageOptional)
					{
						return new string[] { $"Invalid {errorMsgDescribePrice} price: {price}." };
					}
				}
			}
			else
			{
				pricePercentage = defaultPricePercentage/100;
			}

			return null;
		}

		public static decimal TruncateRound(decimal val, int numDecimalPlaces)
		{
			decimal truncatePower = (int)Math.Pow(10, numDecimalPlaces);
			// This removes the decimal after the number of places
			Int64 truncatedAmountP1 = (Int64)(val * truncatePower);
			// Turns it back to a decimal
			decimal truncatedAmountP2 = truncatedAmountP1 / truncatePower;
			// Make sure it's rounded
			decimal fullRoundedAmount = Decimal.Round(truncatedAmountP2, numDecimalPlaces);
			return fullRoundedAmount;
		}

		public static bool IsNoInternetException(Exception e)
		{
			var msg = e.ToString().ToLower();
			if (msg.IndexOf("hostname") >=0 ||
				msg.IndexOf("resolved") >= 0 ||
				msg.IndexOf("no such host")>=0 ||
					(msg.IndexOf("connection")>=0 && msg.IndexOf("failed")>=0)
			)
			{
				return true;
			}

			return false;
		}

		public static async Task RepeatUntilHaveInternet(Func<Task<bool>> fAction)
		{
			DateTime lastComplainedTs = DateTime.Now.AddDays(-1);
			do
			{
				if (await fAction())
				{
					// Done
					break;
				}

				DateTime now = DateTime.Now;
				if ((now - lastComplainedTs).TotalSeconds > 30)
				{
					lastComplainedTs = now;
					Library.AsyncSpeak("No internet.");
				}

				await Task.Delay(1000);

			} while (true);
		}
	}
}
