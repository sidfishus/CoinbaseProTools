using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GetRapidPriceChangeEnabled = System.Func<CoinbasePro.Shared.Types.ProductType, bool>;
using SetRapidPriceChangeEnabled = System.Action<CoinbasePro.Shared.Types.ProductType, bool>;
using static CoinbaseProToolsForm.Library;
using ProductWideSetting = System.Tuple<System.Func<CoinbasePro.Shared.Types.ProductType, bool>,
	System.Action<CoinbasePro.Shared.Types.ProductType, bool>>;
using ProductType = CoinbasePro.Shared.Types.ProductType;

namespace CoinbaseProToolsForm
{
	public class OptionsState
	{
		public bool SpeakPrice;
	}

	public static class Options
	{
		public static IEnumerable<string> CmdLine(OptionsState options, string[] cmdSplit, bool? enable,
			ProductWideSetting getSetRapidPriceChangeEnabled, Func<ProductType> getActiveProduct,
			ProductWideSetting speechEnabledSetting)
		{

			if (Library.StringCompareNoCase(cmdSplit[1], "SPEAKPRICE"))
			{
				bool get = !enable.HasValue;
				if (get) return new string[] { $"Speak price is {EnabledText(options.SpeakPrice)}" };
				options.SpeakPrice = enable.Value;
				return null;
			}
			else if (Library.StringCompareNoCase(cmdSplit[1], "RPC") ||
				Library.StringCompareNoCase(cmdSplit[1], "RAPIDPRICECHANGE"))
			{
				return GetOrSetProductWideSetting(cmdSplit, getActiveProduct, getSetRapidPriceChangeEnabled, enable);
			}
			else if (Library.StringCompareNoCase(cmdSplit[1], "SPEECH") ||
				Library.StringCompareNoCase(cmdSplit[1], "SPEAK"))
			{
				return GetOrSetProductWideSetting(cmdSplit, getActiveProduct, speechEnabledSetting, enable);
			}

			return new string[] { "Unknown option." };
		}

		private static IEnumerable<string> GetOrSetProductWideSetting(string[] cmdSplit, Func<ProductType> getActiveProduct,
			ProductWideSetting setting, bool? enable)
		{
			bool get = !enable.HasValue;

			if (get)
			{
				return Products.DoActionAgainstProductCmdLine(cmdSplit, 2, getActiveProduct, (product, all) =>
				{
					return new string[] { $"{Products.GetProductSpokenName(product)} is "+
						$"{EnabledText(setting.Item1(product))}" };
				}).Item2;
			}
			else
			{
				Products.DoActionAgainstProductCmdLine(cmdSplit, 2, getActiveProduct, (product, all) =>
				{
					setting.Item2(product, enable.Value);
					return null;
				});

				return null;
			}
		}
	}
}
