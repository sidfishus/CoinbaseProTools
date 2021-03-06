using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoinbasePro;
using Types=CoinbasePro.Shared.Types;
using ExceptionFileWriter = System.Action<string>;
using ExceptionUIWriter = System.Action<string>;
using Models = CoinbasePro.Services.Accounts.Models;
using static CoinbaseProToolsForm.Library;

namespace CoinbaseProToolsForm
{
	public static class Account
	{
		public static async Task<IEnumerable<string>> AccountCmd(CoinbaseProClient cbClient, string[] param,
			ExceptionFileWriter exceptionFileWriter, ExceptionUIWriter exceptionUIWriter)
		{
			if (param.Length > 2)
			{
				return new string[] { "Too many parameters: BAL <CUR £ GBP link ETC>" };
			}

			IList<string> rv = null;

			if (param.Length == 1)
			{

				var accountList = await cbClient.AccountsService.GetAllAccountsAsync();
				foreach (var account in accountList)
				{
					// Only show the currencies we trade.
					if (account.Hold > 0 || account.Balance > 0 || Array.Exists(Currency.TradingCurrencies, val => val == account.Currency))
					{
						if (rv == null) rv = new List<string>();
						rv.Add(PrintAccount(account));
					}
				}

			}

			else
			{
				var currency = Currency.CurrencyFromDescr(param[1]);
				if (currency == Types.Currency.Unknown)
				{
					rv = new string[] { $"Unknown currency: {param[1]}" };
				}
				else
				{
					var acc = await cbClient.AccountsService.GetAccountByIdAsync(Currency.CurrencyId(currency));
					rv=new string[] { PrintAccount(acc) };
				}
			}

			return rv;

		}

		static string PrintAccount(CoinbasePro.Services.Accounts.Models.Account account)
		{
			return $"Currency={account.Currency.ToString()}, Available={Decimal.Round(account.Available, 2)}, " +
				$"balance={Decimal.Round(account.Balance, 2)}, hold={Decimal.Round(account.Hold, 2)}";
		}

		public static IEnumerable<string> GetAmountToSpendCmdLine(Models.Account account, string[] cmdSplit, int idx,
			out decimal amountToSpend)
		{
			amountToSpend = 0;

			if (cmdSplit.Length > idx && !StringCompareNoCase(cmdSplit[idx], "all"))
			{

				if (StringCompareNoCase(cmdSplit[idx], "half"))
				{
					amountToSpend = account.Available / 2;
				}
				else if (StringCompareNoCase(cmdSplit[idx], "third"))
				{
					amountToSpend = account.Available / 3;
				}
				else if (StringCompareNoCase(cmdSplit[idx], "quater"))
				{
					amountToSpend = account.Available / 4;
				}
				else
				{
					if (!Decimal.TryParse(cmdSplit[idx], out amountToSpend) || amountToSpend <= 0)
					{
						return new string[] { $"Invalid amount: {cmdSplit[idx]}." };
					}

					if ((account.Available - amountToSpend) < 1) amountToSpend = account.Available;

					if (amountToSpend > account.Available)
					{
						return new string[] { $"{amountToSpend} is more than is available "+
								$"({account.Available})." };
					}
				}
			}
			else
			{
				amountToSpend = account.Available;
			}

			return null;
		}
	}
}
