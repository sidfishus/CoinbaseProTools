using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Types = CoinbasePro.Shared.Types;
using static CoinbaseProToolsForm.Library;

namespace CoinbaseProToolsForm
{
	public static class Currency
	{
		public static readonly string GBP_ID = "036bd08b-a3b2-4ab3-a05b-fcc17591a3e2";
		public static readonly string LINK_ID = "38b93e2c-b678-4a96-8a3c-0f99358ecb70";

		public static readonly Types.Currency[] TradingCurrencies = { Types.Currency.LINK, Types.Currency.GBP };

		public static Types.Currency CurrencyFromDescr(string desc)
		{
			if (desc == "£") return Types.Currency.GBP;
			if (StringCompareNoCase(desc, "LINK")) return Types.Currency.LINK;
			if (StringCompareNoCase(desc, "LNK")) return Types.Currency.LINK;
			if (StringCompareNoCase(desc, "GBP")) return Types.Currency.GBP;

			return Types.Currency.Unknown;
		}

		public static string CurrencyId(Types.Currency currency)
		{
			switch (currency)
			{
				case Types.Currency.GBP:
					return GBP_ID;

				case Types.Currency.LINK:
					return LINK_ID;

				default:
					throw new Exception("Currency ID not known.");
			}
		}
	}
}
