using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static CoinbaseProToolsForm.Library;
using CoinbasePro;
using GetSetOrderBookState = System.Tuple<System.Func<CoinbaseProToolsForm.OrderBookState>, System.Action<CoinbaseProToolsForm.OrderBookState>>;
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

namespace CoinbaseProToolsForm
{
	
	static class Program
	{
		// The products to monitor
		static public readonly ProductType[] products = new ProductType[] {
			ProductType.LinkGbp,
			ProductType.AlgoGbp,
#if !DEBUG
			ProductType.CgldGbp,
			ProductType.NuGbp,
			ProductType.BtcGbp,
#endif
		};


		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			
			//var coinbaseProClient = new CoinbasePro.CoinbaseProClient(authenticator);
			var myClient = new QueuedHttpClient();
			//var cbClient = new CoinbaseProClient(authenticator,myClient);
			var cbClient = new CoinbaseProClient();
			//var cbClient = new CoinbaseProClient(authenticator);

			var state = new State();
			InitState(state, cbClient);
			InitStateDefaults(state);

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			PerpetuallyRunTheForm(state, cbClient);
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

			ProductType activeProduct = ProductType.LinkGbp;
			Func<ProductType> getActiveProduct = () => activeProduct;

			ProductWideSetting rapidPriceChangeSetting = CreateProductWideSetting();
			ProductWideSetting speechSetting = CreateProductWideSetting();

			var twTriggers = CreateTradeWatchTriggers(state, getActiveProduct);
			var newTradesTriggers= CreateNewTradesTriggers(state, rapidPriceChangeSetting.Item1,
				speechSetting.Item1);
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
						twTriggers, rapidPriceChangeSetting, speechSetting);
					exceptionUIWriter = form.exceptionUIWriter;
					eventOutputter = form.eventOutputter;

#if DEBUG
					//tradeHistoryInitialised = true;
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
								() => (productType == getActiveProduct()), complain);
						};
#pragma warning restore 4014

						for (int i = 0; i < Program.products.Length; ++i)
						{
							var prodType = Program.products[i];
							var tradeHistory = allTradeHistory[prodType];
#pragma warning disable 4014
							TradeHistory.LoadApproxLast24Hour(cbClient, tradeHistory, WriteExceptionEverywhere,
								eventOutputter, () => pollForUpdates(prodType, tradeHistory, i==0), getProductStatsInTheBkg, prodType,
								getActiveProduct);
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
			GetProductWideSetting getRapidPriceChangeEnabled, GetProductWideSetting getSpeechEnabled)
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

			var rv = new Dictionary<ProductType, NewTradesTriggerList>();

			Array.ForEach(products, (product) =>
			{

				var triggerList = new NewTradesTriggerList();

				var fIncreaseMsg = priceIncreaseMessageFunc(product);
				var fDecreaseMsg = priceDecreaseMessageFunc(product);

				Func<bool> isEnabledFunc = () => getRapidPriceChangeEnabled(product);
				Func<bool> isSpeechEnabled = () => getSpeechEnabled(product);

				triggerList.triggers.AddRange(new NewTradesTriggerState[]{

					Trigger.CreateRapidPriceChangeTrigger(300*1000, +2.0M, fIncreaseMsg, isEnabledFunc,
						isSpeechEnabled), // Up 2% in 5 minutes or less
					Trigger.CreateRapidPriceChangeTrigger(300*1000, -2.0M, fDecreaseMsg, isEnabledFunc,
							isSpeechEnabled), // Down 2% in 5 minutes or less

					//sidtodo these can be on the summary levels instead.
					Trigger.CreateRapidPriceChangeTrigger(180*1000, +1.0M, fIncreaseMsg, isEnabledFunc,
						isSpeechEnabled), // Up 1% in 2 minutes or less
					Trigger.CreateRapidPriceChangeTrigger(180*1000, -1.0M, fDecreaseMsg, isEnabledFunc,
						isSpeechEnabled), // Down 1% in 2 minutes or less

					Trigger.CreateRapidPriceChangeTrigger(20*1000, +0.05M, fIncreaseMsg, isEnabledFunc,
						isSpeechEnabled), // Up 0.5% in 20 seconds or less
					Trigger.CreateRapidPriceChangeTrigger(20*1000, -0.05M, fDecreaseMsg, isEnabledFunc,
						isSpeechEnabled), // Down 0.5% in 20 seconds or less

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
			Func<ProductType> getProductType)
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

			var rv = new Dictionary<ProductType, SLUpdateTriggerList>();

			Array.ForEach(Program.products, (product) =>
			{
				var list = new SLUpdateTriggerList();
				list.triggers.AddRange(new SLUpdateTriggerState[]{
					Trigger.CreateBullRunTradeWatchTrigger(product),
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

		//sidtodo this isn't working: creating 2 copies of the dictionary :/
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