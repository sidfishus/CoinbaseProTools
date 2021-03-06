using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static CoinbaseProToolsForm.Library;
using CoinbasePro;
using Types = CoinbasePro.Shared.Types;
using GetSetProductsState = System.Tuple<System.Func<CoinbaseProToolsForm.ProductsState>, System.Action<CoinbaseProToolsForm.ProductsState>>;
using ProductStatsDictionary = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType, CoinbasePro.Services.Products.Types.ProductStats>;
using TradeSummaryLevels = System.Collections.Generic.Dictionary<int /* Summary period in units */, CoinbaseProToolsForm.TradeSummaryState>;
using ExceptionFileWriter = System.Action<string>;
using System.Runtime.CompilerServices;
using IHttpClient = CoinbasePro.Network.HttpClient.IHttpClient;
using HttpClient = CoinbasePro.Network.HttpClient.HttpClient;
using CoinbasePro.Network.HttpClient;
using System.Net.Http;
using System.Threading;
using ExceptionUIWriter = System.Action<string>;
using EventOutputter = System.Action<System.Collections.Generic.IEnumerable<System.Tuple<string, System.DateTimeOffset?>>>;
using EventOutput = System.Tuple<string, System.DateTimeOffset?>;
using CBProductTrade = CoinbasePro.Services.Products.Models.ProductTrade;
using ProductType = CoinbasePro.Shared.Types.ProductType;
using AllTradeHistory = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType,
	CoinbaseProToolsForm.TradeHistoryState>;
using AddNewTradeTrigger = System.Action<CoinbasePro.Shared.Types.ProductType, CoinbaseProToolsForm.NewTradesTriggerState>;
using RemoveNewTradeTrigger = System.Action<CoinbasePro.Shared.Types.ProductType,
	CoinbaseProToolsForm.NewTradesTriggerList,
	int[]>;
using AddSLUpdateTrigger = System.Action<CoinbasePro.Shared.Types.ProductType, CoinbaseProToolsForm.SLUpdateTriggerState>;
using static CoinbaseProToolsForm.Products;
using Debug = System.Diagnostics.Debug;
using GetProductWideSetting = System.Func<CoinbasePro.Shared.Types.ProductType, bool>;
using SetProductWideSetting = System.Action<CoinbasePro.Shared.Types.ProductType, bool>;
using ProductWideSetting = System.Tuple<System.Func<CoinbasePro.Shared.Types.ProductType, bool>,
	System.Action<CoinbasePro.Shared.Types.ProductType, bool>>;
using System.Configuration;
using Authenticator = CoinbasePro.Network.Authentication.Authenticator;

namespace CoinbaseProToolsForm
{
	
	static class Program
	{
		// The products to monitor
		static public readonly ProductType[] products = new ProductType[] {
			ProductType.LinkGbp,
#if !DEBUG
			//ProductType.AlgoGbp,
			//ProductType.GrtGbp,
			//ProductType.CgldGbp,
			//ProductType.NuGbp,
			//ProductType.BtcGbp,
#endif
		};


		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
		
			var myClient = new QueuedHttpClient();
			var cbClient = new CoinbaseProClient(CreateAuthenticator(),myClient);

			var state = new State();
			InitState(state, cbClient);
			InitStateDefaults(state);

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			PerpetuallyRunTheForm(state, cbClient);
		}

		static Authenticator CreateAuthenticator()
		{
			ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
			configMap.ExeConfigFilename = @"..\..\secret.config";
			Configuration config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);

			AppSettingsSection section = (AppSettingsSection)config.GetSection("coinbase");
			var apiKey= (string)section.Settings["apiKey"].Value;
			var unsignedSignature= (string)section.Settings["unsignedSignature"].Value;
			var passPhrase= (string)section.Settings["passPhrase"].Value;

			var authenticator = new CoinbasePro.Network.Authentication.Authenticator(apiKey, unsignedSignature, passPhrase);

			return authenticator;
		}

		static string FormatExceptionText(string text)
		{
			return $"{DateTime.Now.ToString()} {text}{Environment.NewLine}";
		}

		static ExceptionFileWriter CreateExceptionFileWriter()
		{
			var ignoreDuplicateStrings = Library.IgnoreDuplicates<string>(Library.StringCompareNoCase, 15);
			return Library.SynchronisedFileAppender("exceptions.txt", FormatExceptionText, ignoreDuplicateStrings);
		}

		static void PerpetuallyRunTheForm(State state, CoinbaseProClient cbClient)
		{

			WebSocketState webSocketState = new WebSocketState();

			bool networkTrafficEnabled = true;
			Action<bool> fEnableNetworkTraffic = (enable) => networkTrafficEnabled = enable;
			Func<bool> fNetworkTrafficEnabled = () => networkTrafficEnabled;

			ProductType activeProduct = ProductType.LinkGbp;
			Func<ProductType> getActiveProduct = () => activeProduct;

			ProductWideSetting rapidPriceChangeUpSetting = CreateProductWideSetting();
			ProductWideSetting rapidPriceChangeDownSetting = CreateProductWideSetting();
			ProductWideSetting speechSetting = CreateProductWideSetting();
			ProductWideSetting rapidLargeVolumeUp = CreateProductWideSetting();
			ProductWideSetting rapidLargeVolumeDown = CreateProductWideSetting(); //sidtodo not in use yet
			ProductWideSetting steadyPriceIncreaseTrigger = CreateProductWideSetting();
			ProductWideSetting steadyPriceDecreaseTrigger = CreateProductWideSetting();

			var twTriggers = CreateTradeWatchTriggers(state, getActiveProduct,
				steadyPriceIncreaseTrigger.Item1, steadyPriceDecreaseTrigger.Item1, speechSetting.Item1);
			var newTradesTriggers= CreateNewTradesTriggers(state, rapidPriceChangeUpSetting.Item1,
				rapidPriceChangeDownSetting.Item1,
				speechSetting.Item1, rapidLargeVolumeUp.Item1, rapidLargeVolumeDown.Item1);
			var addNewTradesTrigger = CreateAddNewTradesTrigger(newTradesTriggers);
			var removeNewTradesTrigger= CreateRemoveNewTradesTrigger();
			var exceptionFileWriter = CreateExceptionFileWriter();

			ExceptionUIWriter exceptionUIWriter = null;
			EventOutputter eventOutputter = null;
			Action clearEventWindow = () =>
			{
				eventOutputter?.Invoke(null);
			};
			Action<Exception> WriteExceptionEverywhere = (exception) =>
			{
				var text = exception.ToString();
				exceptionFileWriter(text);
				exceptionUIWriter?.Invoke(text);
			};

			var getProductStatsInTheBkg = GetProductStatsInTheBkg(state, cbClient, WriteExceptionEverywhere);
			getProductStatsInTheBkg(); // Initialise the product stats

			var allTradeHistory = ConstructTradeHistory();
			Func<TradeHistoryState> getTradeHistory = () => allTradeHistory[getActiveProduct()];

			Func<ProductType, bool> setActiveProduct = (productType) =>
			{
				bool rv = (activeProduct != productType);
				if (rv)
				{
					activeProduct = productType;
					clearEventWindow();
					if (eventOutputter != null)
					{
						var output=TradeHistory.OutputTradeHistory(getTradeHistory(), activeProduct,
							getProductStatsInTheBkg()[activeProduct]);
						eventOutputter(output);
					}
				}

				return rv;
			};

			Form1 form = null;
			Func<Form1> GetTheActiveForm = () =>
			{
				if (form != null && !form.IsDisposed && form.IsHandleCreated) return form;

				return null;
			};

			ConfigureHandleExceptionsInTheApp(exceptionFileWriter);

			Exception previousException =null;

			//sidtodo here: as well as remembering for different time slots, calculate average per different time slots.
			bool tradeHistoryInitialised = false;

			LockedByRef<InProgressCommand> inProgressCmd = new LockedByRef<InProgressCommand>();

			for (bool startForm = true; startForm == true;)
			{
				//sidtodo test writing to event log when form is closed.

				try
				{

					if (form != null && !form.IsDisposed)
					{
						form.Dispose();
					}

					form = new Form1(state, cbClient, GetTheActiveForm, previousException, exceptionFileWriter,
						getProductStatsInTheBkg, WriteExceptionEverywhere,
						getTradeHistory, getActiveProduct, setActiveProduct,
						addNewTradesTrigger, removeNewTradesTrigger, newTradesTriggers,
						twTriggers, rapidPriceChangeUpSetting, rapidPriceChangeDownSetting,
						speechSetting, rapidLargeVolumeUp, rapidLargeVolumeDown,
						inProgressCmd, webSocketState, fNetworkTrafficEnabled, fEnableNetworkTraffic,
						steadyPriceIncreaseTrigger, steadyPriceDecreaseTrigger);
					exceptionUIWriter = form.exceptionUIWriter;
					eventOutputter = form.eventOutputter;

#if DEBUG
					tradeHistoryInitialised = true; //sidtodo remove
#endif

					if (!tradeHistoryInitialised)
					{
						tradeHistoryInitialised = true;

#pragma warning disable 4014
						Action<ProductType, TradeHistoryState,bool> pollForUpdates = (productType, tradeHistory, complain) => {

							var newTradesCallback = NewTradesCallback(newTradesTriggers[productType],
								WriteExceptionEverywhere, () => eventOutputter, getProductStatsInTheBkg);

							var twSummaryLevelUpdateCallback = TradeWatchSummaryLevelUpdateCallback(
								twTriggers[productType], WriteExceptionEverywhere, () => eventOutputter, getProductStatsInTheBkg,
								getActiveProduct);

							PollForUpdates.PollForUpdatesAsync(cbClient, WriteExceptionEverywhere, tradeHistory, eventOutputter,
								getProductStatsInTheBkg, twSummaryLevelUpdateCallback, newTradesCallback, productType,
								() => (productType == getActiveProduct()), complain, fNetworkTrafficEnabled);
						};
#pragma warning restore 4014

						for (int i = 0; i < Program.products.Length; ++i)
						{
							bool isFirst = i == 0;
							var prodType = Program.products[i];
							var tradeHistory = allTradeHistory[prodType];
#pragma warning disable 4014
							TradeHistory.LoadApproxLast24Hour(cbClient, tradeHistory, WriteExceptionEverywhere,
								eventOutputter, () => pollForUpdates(prodType, tradeHistory, isFirst), getProductStatsInTheBkg,
								prodType,getActiveProduct);
#pragma warning restore 4014
						}
					}
					Application.Run(form);
					startForm = false;
				}
				catch (Exception e)
				{
					exceptionUIWriter = null;
					previousException = e;
					exceptionFileWriter(e.ToString());
					startForm = true;
				}
			}

		}

		private static void ConfigureHandleExceptionsInTheApp(ExceptionFileWriter exceptionFileWriter)
		{
			// This removes the unhandled exception dialog. It's irrelevant, because we are catching errors.
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
			AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
			{
				// If this happens, we can't do anything about it except for writing an exception out.
				// Always catch exceptions within non UI thread code. We must not reach here.
				exceptionFileWriter(e.ExceptionObject.ToString());
			};
		}

		static Dictionary<ProductType,NewTradesTriggerList> CreateNewTradesTriggers(State state,
			GetProductWideSetting getRapidPriceChangeUpEnabled,
			GetProductWideSetting getRapidPriceChangeDownEnabled,
			GetProductWideSetting getSpeechEnabled,
			GetProductWideSetting getRapidLargeVolumeUp, GetProductWideSetting getRapidLargeVolumeDown)
		{

			Func<ProductType, Func<int, decimal, string>> priceDecreaseMessageFunc = (productType) =>
			  {
				  return (timeDiff, priceChangeRatio) =>
				  {
					  return $"{Products.GetProductSpokenName(productType)} rapid price decrease, "+
						$"{Decimal.Round(priceChangeRatio, 2)}% in {Math.Abs(timeDiff) / 1000} seconds";
				  };
			  };

			Func<ProductType, Func<int, decimal, string>> priceIncreaseMessageFunc = (productType) =>
			{
				return (timeDiff, priceChangeRatio) =>
				{
					return $"{Products.GetProductSpokenName(productType)} Rapid price increase, "+
						$"{Decimal.Round(priceChangeRatio, 2)}% in {Math.Abs(timeDiff) / 1000} seconds";
				};
			};

			Func<ProductType, Func<int, decimal, string>> largeVolumeIncreaseMessageFunc = (productType) =>
			{
				return (timeDiff, volChangeRatio) =>
				{
					return $"{Products.GetProductSpokenName(productType)} large buy, " +
						$"{Decimal.Round(volChangeRatio, 2)}% in {Math.Abs(timeDiff) / 1000} seconds";
				};
			};

			Func<ProductType, Func<int, decimal, string>> largeVolumeDecreaseMessageFunc = (productType) =>
			{
				return (timeDiff, volChangeRatio) =>
				{
					return $"{Products.GetProductSpokenName(productType)} large sell, " +
						$"{Decimal.Round(volChangeRatio, 2)}% in {Math.Abs(timeDiff) / 1000} seconds";
				};
			};

			var rv = new Dictionary<ProductType, NewTradesTriggerList>();

			Array.ForEach(products, (product) =>
			{

				var triggerList = new NewTradesTriggerList();

				var fRapidIncreaseMsg = priceIncreaseMessageFunc(product);
				var fRapidDecreaseMsg = priceDecreaseMessageFunc(product);

				Func<int,decimal,string> fLargeBuyMsg = largeVolumeIncreaseMessageFunc(product);
				Func<int, decimal, string> fLargeSellMsg = largeVolumeDecreaseMessageFunc(product);

				Func<bool> isRPCUEnabledFunc = () => getRapidPriceChangeUpEnabled(product);
				Func<bool> isRPCDEnabledFunc = () => getRapidPriceChangeDownEnabled(product);
				Func<bool> isSpeechEnabled = () => getSpeechEnabled(product);
				Func<bool> largeBuyIsEnabled = () => getRapidLargeVolumeUp(product);
				Func<bool> largeSellIsEnabled = () => getRapidLargeVolumeDown(product);

				var productInfo = Products.productInfo[product];

				triggerList.triggers.AddRange(new NewTradesTriggerState[]{

					Trigger.CreateRapidPriceChangeTrigger(300*1000, +2.0M*productInfo.volatilityFactor,
						fRapidIncreaseMsg, isRPCUEnabledFunc, isSpeechEnabled),
					Trigger.CreateRapidPriceChangeTrigger(300*1000, -2.0M*productInfo.volatilityFactor,
						fRapidDecreaseMsg, isRPCDEnabledFunc,isSpeechEnabled),

					Trigger.CreateRapidPriceChangeTrigger(30*1000, +1.0M*productInfo.volatilityFactor,
						fRapidIncreaseMsg, isRPCUEnabledFunc,isSpeechEnabled),
					Trigger.CreateRapidPriceChangeTrigger(30*1000, -1.0M*productInfo.volatilityFactor,
						fRapidDecreaseMsg, isRPCDEnabledFunc,isSpeechEnabled),

					//sidtodo volatility factor for volume????
					//sidtodo test these for algorand

					Trigger.CreateRapidBuyOrSell(5*1000,+0.5M,fLargeBuyMsg,largeBuyIsEnabled,isSpeechEnabled,product),
					Trigger.CreateRapidBuyOrSell(5*1000,-0.5M,fLargeSellMsg,largeSellIsEnabled,isSpeechEnabled,product),

					Trigger.CreateRapidBuyOrSell(10*1000,+1,fLargeBuyMsg,largeBuyIsEnabled,isSpeechEnabled,product),
					Trigger.CreateRapidBuyOrSell(10*1000,-1,fLargeSellMsg,largeSellIsEnabled,isSpeechEnabled,product),

					Trigger.CreateRapidBuyOrSell(60*1000,+2,fLargeBuyMsg,largeBuyIsEnabled,isSpeechEnabled,product),
					Trigger.CreateRapidBuyOrSell(60*1000,-2,fLargeSellMsg,largeSellIsEnabled,isSpeechEnabled,product),
#if DEBUG

					//Trigger.CreateRapidPriceChangeTrigger(100*1000, +0.003M, fIncreaseMsg),
					//Trigger.CreateRapidPriceChangeTrigger(100*1000, -0.003M, fDecreaseMsg),
#endif
				});

				triggerList.triggers.AddRange(Trigger.PersistReadNewTradesTriggers(product));

				rv.Add(product, triggerList);
			});

			return rv;
		}

		static Dictionary<ProductType, SLUpdateTriggerList> CreateTradeWatchTriggers(State state,
			Func<ProductType> getProductType, GetProductWideSetting steadyPriceIncreaseTrigger,
			GetProductWideSetting steadyPriceDecreaseTrigger, GetProductWideSetting getSpeechEnabled)
		{

			// The order is important. If one rule triggers, lesser triggers won't take affect. May
			// want to control this behaviour at a later date.

			//sidtodo parameterise these
			//return new SLUpdateTriggerState[] { Trigger.CreateBullRunTradeWatchTrigger(),
			//	Trigger.CreateLowPriceReachValueTrigger(17.4999M, "Price has dropped less than £17.50"),
			//	Trigger.CreateLowPriceReachValueTrigger(17.9999M, "Price has dropped less than £18.00"),
			//	Trigger.CreateHighPriceReachValueTrigger(20M, "Price has reached £20.00"),
			//	Trigger.CreateLargeVolumeIncreaseOrDecreaseTrigger(),
			//	Trigger.CreatePriceConsistentlyGoingUpTrigger(),
			//	Trigger.CreatePriceConsistentlyGoingDownTrigger(),
			//	Trigger.CreateSpeakPriceTrigger(state),
			//};

			Func<ProductType, Func<int, decimal, string>> steadyPriceDecreaseMessageFunc = (productType) =>
			{
				return (timeDiff, priceChangeRatio) =>
				{
					return $"{Products.GetProductSpokenName(productType)} steady price decrease, " +
					 $"{Decimal.Round(priceChangeRatio, 2)}% in {Math.Abs(timeDiff) / (1000*60)} minutes";
				};
			};

			Func<ProductType, Func<int, decimal, string>> steadyPriceIncreaseMessageFunc = (productType) =>
			{
				return (timeDiff, priceChangeRatio) =>
				{
					return $"{Products.GetProductSpokenName(productType)} steady price increase, " +
						$"{Decimal.Round(priceChangeRatio, 2)}% in {Math.Abs(timeDiff) / (1000*60)} minutes";
				};
			};
			
			var rv = new Dictionary<ProductType, SLUpdateTriggerList>();

			Array.ForEach(Program.products, (product) =>
			{

				Func<bool> isSpeechEnabled = () => getSpeechEnabled(product);

				var fSteadyIncreaseMsg = steadyPriceIncreaseMessageFunc(product);
				var fSteadyDecreaseMsg = steadyPriceDecreaseMessageFunc(product);
							  
				Func<bool> steadyPriceIncreaseEnabled = () => steadyPriceIncreaseTrigger(product);
				Func<bool> steadyPriceDecreaseEnabled = () => steadyPriceDecreaseTrigger(product);

				var list = new SLUpdateTriggerList();
				list.triggers.AddRange(new SLUpdateTriggerState[]{
					Trigger.CreateBullRunTradeWatchTrigger(product),
					//sidtodo - this could be a lot more efficient.
					Trigger.CreateSteadyPriceChangeTrigger(120*60,+5,fSteadyIncreaseMsg,steadyPriceIncreaseEnabled,isSpeechEnabled),
					Trigger.CreateSteadyPriceChangeTrigger(120*60,-5,fSteadyDecreaseMsg,steadyPriceDecreaseEnabled,isSpeechEnabled),
					Trigger.CreateSteadyPriceChangeTrigger(60*60,+3,fSteadyIncreaseMsg,steadyPriceIncreaseEnabled,isSpeechEnabled),
					Trigger.CreateSteadyPriceChangeTrigger(60*60,-3,fSteadyDecreaseMsg,steadyPriceDecreaseEnabled,isSpeechEnabled),
					Trigger.CreateSpeakPriceTrigger(state, getProductType, product)
				});

				rv.Add(product, list);
			});

			Debug.Assert(rv.Count > 0);
			return rv;
		}

		static void InitStateDefaults(State state)
		{
			state.options.SpeakPrice = false;
		}

		static void InitState(State state, CoinbaseProClient cbClient)
		{
			state.productsState = new ProductsState();
			state.options = new OptionsState();
		}

		static AllTradeHistory ConstructTradeHistory()
		{
			var history = new AllTradeHistory();
			for (int i = 0; i < Program.products.Length; ++i)
			{
				history.Add(Program.products[i], new TradeHistoryState());
			}

			return history;
		}

		static Action<TradeSummaryLevels, List<CBProductTrade>> NewTradesCallback(
			NewTradesTriggerList triggers, Action<Exception> HandleExceptions, Func<EventOutputter> Output,
			Func<ProductStatsDictionary> GetProductStats)
		{
			return (tradeSummaryLevels, recentTrades) =>
			{
				Trigger.NewTradesCallback(triggers, tradeSummaryLevels,
					GetProductStats, HandleExceptions, Output(), recentTrades);
			};
		}

		static Action<TradeSummaryLevels> TradeWatchSummaryLevelUpdateCallback(
			SLUpdateTriggerList triggers, Action<Exception> HandleExceptions, Func<EventOutputter> Output,
			Func<ProductStatsDictionary> GetProductStats, Func<ProductType> getActiveProduct)
		{
			return (tradeSummaryLevels) =>
			{
				Trigger.TradeWatchSummaryLevelUpdateCallback(triggers, tradeSummaryLevels,
					GetProductStats, HandleExceptions, Output());
			};
		}

		static Func<ProductStatsDictionary> GetProductStatsInTheBkg(State state,
			CoinbaseProClient cbClient, Action<Exception> HandleExceptions)
		{
			return () =>
			{
				int refreshEvery10Minutes = 60 * 10;
				return Products.GetProductStatsInTheBkg(state.productsState, cbClient, Program.products,
					refreshEvery10Minutes, HandleExceptions);
			};
		}

		// This assumes the new trade list has already been locked by teh caller.
		static AddNewTradeTrigger CreateAddNewTradesTrigger(
			Dictionary<ProductType,NewTradesTriggerList> triggerListDictionary)
		{
			return (productType,trigger) => {

				var triggerList = triggerListDictionary[productType];

				triggerList.triggers.Add(trigger);
				Trigger.PersistWriteNewTradesTriggersAssumingAlreadyLocked(productType, triggerList);
			};
		}

		static ProductWideSetting CreateProductWideSetting()
		{
			Dictionary<ProductType, bool> enabledDictionary = new Dictionary<ProductType, bool>();
			Array.ForEach(Program.products, (product) =>
			{
				enabledDictionary.Add(product,true);
			});

			GetProductWideSetting getFunc = (productType) =>
			{
				return enabledDictionary[productType] == true;
			};
			SetProductWideSetting setFunc = (productType, enabled) =>
			{
				enabledDictionary[productType] = enabled;
			};


			var rv = new ProductWideSetting(getFunc,setFunc);

			return rv;
		}

		// This assumes the new trade list has already been locked by teh caller.
		static RemoveNewTradeTrigger CreateRemoveNewTradesTrigger()
		{
			return (productType,triggerList, indexList /* null means remove all */) => {

				var temp = triggerList.triggers;
				triggerList.triggers = new List<NewTradesTriggerState>();
				if (indexList != null)
				{
					for (int i = 0; i < temp.Count - 1; ++i)
					{
						if (!indexList.Contains(i))
						{
							triggerList.triggers.Add(temp[i]);
						}
					}
				}
				
				Trigger.PersistWriteNewTradesTriggersAssumingAlreadyLocked(productType, triggerList);
			};
		}
	}
}