﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
	}
}
