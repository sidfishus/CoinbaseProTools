using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoinbasePro;
using ProductStats = CoinbasePro.Services.Products.Types.ProductStats;
using GetSetProductsState = System.Tuple<System.Func<CoinbaseProToolsForm.ProductsState>, System.Action<CoinbaseProToolsForm.ProductsState>>;
using ProductType = CoinbasePro.Shared.Types.ProductType;
using ProductStatsDictionary = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType, CoinbasePro.Services.Products.Types.ProductStats>;
using ExceptionFileWriter = System.Action<string>;
using ExceptionUIWriter = System.Action<string>;
using ProductInfoDictionary = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType, CoinbaseProToolsForm.ProductInfo>;
using static CoinbaseProToolsForm.Library;

namespace CoinbaseProToolsForm
{
	public class ProductsState
	{
		public Task<ProductStatsDictionary> productStatsTask;
		public ProductStatsDictionary productStats;
		public readonly object productStateLock=new object();
		public DateTime productStatsLastRefresh;
	}

	public class ProductInfo
	{
		public string name;
		public string spokenName;
		public string[] otherNames;
		public ProductType productType;
		public Func<decimal, string> fOutputPrice;
		public Func<decimal, string> fOutputVolume;
		public Func<decimal, string> fOutputNumberOfTrades;
		public Func<decimal, string> fSpeakPrice;
		public int numPagesForApprox24Hour;
		public decimal volatilityFactor; // The higher = more volatile. Link is 1.0
	}

	public static class Products
	{
		public static readonly ProductInfoDictionary productInfo= InitialiseProductInfo();

		public static string GetProductSpokenName(ProductType prodType)
		{
			return productInfo[prodType].spokenName;
		}

		public static IEnumerable<string> CmdLine(string[] cmdSplit,
			Func<ProductType, bool> setActiveProduct)
		{
			if (StringCompareNoCase(cmdSplit[1], "SETACTIVE") ||
				StringCompareNoCase(cmdSplit[1], "SETPRODUCT") ||
				StringCompareNoCase(cmdSplit[1], "SETPROD"))
			{
				return SetActiveProductCmdLine(cmdSplit, setActiveProduct);
			}

			return new string[] { $"Invalid command: {cmdSplit[1]}"};
		}

		private static IEnumerable<string> SetActiveProductCmdLine(string[] cmdSplit,
			Func<ProductType, bool> setActiveProduct)
		{
			if (cmdSplit.Length == 2)
			{
				return new string[] { "Missing the product name." };
			}
			var product = FindProductFromName(cmdSplit[2]);
			if (product == null)
			{
				return new string[] { $"Invalid product name '{cmdSplit[2]}'." };
			}

			if (setActiveProduct(product.productType))
			{
				return new string[] { $"Product changed to {product.spokenName}." };
			}

			return new string[] { "No change."};
		}

		public static ProductInfo FindProductFromName(string name)
		{
			foreach (var kvp in productInfo)
			{
				var prodInfo = kvp.Value;
				if (StringCompareNoCase(name, prodInfo.name))
				{
					return prodInfo;
				}
				if (StringCompareNoCase(name, prodInfo.spokenName.Replace(" ","")))
				{
					return prodInfo;
				}
				
				for (int i = 0; i < prodInfo.otherNames?.Length; ++i)
				{
					if (StringCompareNoCase(name, prodInfo.otherNames[i]))
					{
						return prodInfo;
					}
				}
			}

			return null;
		}

		private static Func<decimal,string> SpeakPriceGbp(int decimalPlaces)
		{
			return (price) =>
			{
				return $"£{Decimal.Round(price, decimalPlaces)}";
			};
		}

		private static ProductInfoDictionary InitialiseProductInfo()
		{
			var info = new ProductInfoDictionary();

			var chainLink = new ProductInfo { productType= ProductType.LinkGbp, name = "Chainlink",
				spokenName ="Link", otherNames = new string[] { "lnk", "linkgbp" } };

			chainLink.fOutputPrice = (price) => Decimal.Round(price, 2).ToString().PadLeft(6, ' ');
			chainLink.fOutputVolume = (volume) => Decimal.Round(volume, 0).ToString().PadLeft(7, ' ');
			chainLink.fOutputNumberOfTrades = (numTrades) => numTrades.ToString().PadLeft(5, ' ');
			chainLink.fSpeakPrice = SpeakPriceGbp(2);
			chainLink.volatilityFactor = 1;
			chainLink.numPagesForApprox24Hour =40;

			info.Add(ProductType.LinkGbp, chainLink);

			var nucypher = new ProductInfo { productType = ProductType.NuGbp, name = "NuCypher", spokenName="New cypher", otherNames = new string[]{ "nu", "nugbp", "cypher" } };
			nucypher.fOutputPrice = (price) => Decimal.Round(price,4).ToString();
			nucypher.fOutputVolume = (volume) => Decimal.Round(volume, 0).ToString().PadLeft(9, ' ');
			nucypher.fOutputNumberOfTrades = (numTrades) => numTrades.ToString().PadLeft(5, ' ');
			nucypher.fSpeakPrice = SpeakPriceGbp(4);
			nucypher.numPagesForApprox24Hour = 40;
			nucypher.volatilityFactor = 1; //sidtodo don't know
			info.Add(ProductType.NuGbp, nucypher);

			var algo = new ProductInfo { productType = ProductType.AlgoGbp, name = "Algorand", spokenName = "Algorand", otherNames = new string[] { "algo", "alto", "algogbp" } };
			algo.fOutputPrice = (price) => Decimal.Round(price, 5).ToString();
			algo.fOutputVolume = (volume) => Decimal.Round(volume, 0).ToString().PadLeft(9, ' ');
			algo.fOutputNumberOfTrades = (numTrades) => numTrades.ToString().PadLeft(5, ' ');
			algo.fSpeakPrice = SpeakPriceGbp(4);
			algo.numPagesForApprox24Hour = 40;
			algo.volatilityFactor = 2M;
			info.Add(ProductType.AlgoGbp, algo);

			var bitcoin = new ProductInfo { productType = ProductType.BtcGbp, name = "Bitcoin", spokenName = "Bitcoin", otherNames = new string[] { "btc", "btcgbp" } };
			bitcoin.fOutputPrice = (price) => Decimal.Round(price, 0).ToString();
			bitcoin.fOutputVolume = (volume) => Decimal.Round(volume, 4).ToString().PadLeft(9, ' ');
			bitcoin.fOutputNumberOfTrades = (numTrades) => numTrades.ToString().PadLeft(5, ' ');
			bitcoin.fSpeakPrice = SpeakPriceGbp(0);
			bitcoin.numPagesForApprox24Hour = 200;
			bitcoin.volatilityFactor = 1; //sidtodo don't know
			info.Add(ProductType.BtcGbp, bitcoin);

			var celo = new ProductInfo { productType = ProductType.CgldGbp, name = "Celo", spokenName = "Celo", otherNames = new string[] { "cgld", "cgldgbp" } };
			celo.fOutputPrice = (price) => Decimal.Round(price, 3).ToString();
			celo.fOutputVolume = (volume) => Decimal.Round(volume).ToString().PadLeft(8, ' ');
			celo.fOutputNumberOfTrades = (numTrades) => numTrades.ToString().PadLeft(5, ' ');
			celo.fSpeakPrice = SpeakPriceGbp(2);
			celo.numPagesForApprox24Hour = 40;
			celo.volatilityFactor = 1; //sidtodo don't know
			info.Add(ProductType.CgldGbp, celo);

			return info;
		}
		
		private static async Task<ProductStatsDictionary> GetProductStats(ProductType[] products,
			CoinbaseProClient cbClient)
		{

			var prodStats = new ProductStatsDictionary();

			for (int i = 0; i < products.Length; ++i)
			{
				var prod = products[i];
				prodStats[prod]=await cbClient.ProductsService.GetProductStatsAsync(prod);
			}

			return prodStats;
		}
		
		public static ProductStatsDictionary GetProductStatsInTheBkg(ProductsState state,
			CoinbaseProClient cbClient, ProductType[] products, int refreshIntervalSecs,
			Action<Exception> HandleExceptions)
		{
			if (state.productStatsTask != null)
			{
				if (state.productStatsTask?.IsCompleted == true)
				{
					lock (state.productStateLock)
					{
						// We have the results.
						if (state.productStatsTask?.IsCompleted == true)
						{
							//sidtodo here print out the 24 hour stats???
							state.productStats = state.productStatsTask.Result;
							state.productStatsTask.Dispose();
							state.productStatsTask = null;
							state.productStatsLastRefresh = DateTime.Now;
						}

						return state.productStats;
					}
				}

				if (state.productStatsTask?.IsFaulted == true)
				{
					lock (state.productStateLock)
					{
						if (state.productStatsTask?.IsFaulted == true)
						{
							// Try and get the stats again.
							state.productStatsTask.Dispose();
							state.productStatsTask = GetProductStats(products, cbClient);

							HandleExceptions(state.productStatsTask.Exception);
						}
					}
				}
			}
			else
			{
				if (ProductStatsAreOutOfDate(state, refreshIntervalSecs))
				{
					lock (state.productStateLock)
					{
						if (state.productStatsTask == null)
						{
							state.productStatsTask = GetProductStats(products, cbClient);
						}
					}
				}
			}

			return state.productStats;
		}

		private static bool ProductStatsAreOutOfDate(ProductsState state, int refreshIntervalSecs)
		{
			return (state.productStats==null || state.productStatsLastRefresh==null ||
				(DateTime.Now- state.productStatsLastRefresh).TotalSeconds>= refreshIntervalSecs);
		}

		public static Tuple<bool,IEnumerable<string>> DoActionAgainstProductCmdLine(string[] cmdSplit, int prodOrOptionIndex,
			Func<ProductType> getActiveProduct, Func<ProductType, bool, IEnumerable<string>> func)
		{
			
			ProductType? product=null;

			if (cmdSplit.Length > prodOrOptionIndex)
			{
				if (StringCompareNoCase(cmdSplit[prodOrOptionIndex], "ALL"))
				{
				}
				else
				{
					// Try and find the product
					var prodInfo = FindProductFromName(cmdSplit[prodOrOptionIndex]);

					if (prodInfo == null)
					{
						return new Tuple<bool, IEnumerable<string>>(false, new string[] { $"Unknown option {cmdSplit[prodOrOptionIndex]}" });
					}

					product = prodInfo.productType;
				}
			}
			else
			{
				product = getActiveProduct();
			}

			var list = new List<string>();

			if (!product.HasValue)
			{
				Array.ForEach(Program.products, (iterProd) =>
				{
					var rv=func(iterProd, true);
					if (rv != null) list.AddRange(rv);
				});
			}
			else
			{
				var rv = func(product.Value, false);
				if (rv != null) list.AddRange(rv);
			}

			return new Tuple<bool, IEnumerable<string>>(true,list);
		}
	}
}
