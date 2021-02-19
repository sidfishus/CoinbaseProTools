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
			ProductWideSetting getSetRapidPriceChangeUpEnabled, ProductWideSetting getSetRapidPriceChangeDownEnabled,
			Func<ProductType> getActiveProduct,
			ProductWideSetting speechEnabledSetting,
			ProductWideSetting getSetRapidLargeBuySetting, ProductWideSetting getSetRapidLargeSellSetting)
		{

			if (Library.StringCompareNoCase(cmdSplit[1], "SPEAKPRICE"))
			{
				bool get = !enable.HasValue;
				if (get) return new string[] { $"Speak price is {EnabledText(options.SpeakPrice)}" };
				options.SpeakPrice = enable.Value;
				return null;
			}
			else if (Library.StringCompareNoCase(cmdSplit[1], "RPCU") ||
				Library.StringCompareNoCase(cmdSplit[1], "RAPIDPRICECHANGEUP"))
			{
				return GetOrSetProductWideSetting(cmdSplit, getActiveProduct, getSetRapidPriceChangeUpEnabled, enable);
			}
			else if (Library.StringCompareNoCase(cmdSplit[1], "RPCD") ||
				Library.StringCompareNoCase(cmdSplit[1], "RAPIDPRICECHANGEDOWN"))
			{
				return GetOrSetProductWideSetting(cmdSplit, getActiveProduct, getSetRapidPriceChangeDownEnabled, enable);
			}
			else if (Library.StringCompareNoCase(cmdSplit[1], "SPEECH") ||
				Library.StringCompareNoCase(cmdSplit[1], "SPEAK"))
			{
				return GetOrSetProductWideSetting(cmdSplit, getActiveProduct, speechEnabledSetting, enable);
			}
			else if (Library.StringCompareNoCase(cmdSplit[1], "RLB") ||
				Library.StringCompareNoCase(cmdSplit[1], "RAPIDLARGEBUY"))
			{
				return GetOrSetProductWideSetting(cmdSplit, getActiveProduct, getSetRapidLargeBuySetting, enable);
			}
			else if (Library.StringCompareNoCase(cmdSplit[1], "RLS") ||
				Library.StringCompareNoCase(cmdSplit[1], "RAPIDLARGESELL"))
			{
				return GetOrSetProductWideSetting(cmdSplit, getActiveProduct, getSetRapidLargeSellSetting, enable);
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
