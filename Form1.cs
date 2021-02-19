using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
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
using ExceptionUIWriter = System.Action<string>;
using EventOutputter = System.Action<System.Collections.Generic.IEnumerable<System.Tuple<string, System.DateTimeOffset?>>>;
using EventOutput = System.Tuple<string, System.DateTimeOffset?>;
using NewTradesCallback = System.Action<System.Collections.Generic.Dictionary<int /* Summary period in units */,
	CoinbaseProToolsForm.TradeSummaryState>,
	System.Collections.Generic.List<CoinbasePro.Services.Products.Models.ProductTrade>>;
using AllTradeHistory = System.Collections.Generic.Dictionary<CoinbasePro.Shared.Types.ProductType,
	CoinbaseProToolsForm.TradeHistoryState>;
using ProductType = CoinbasePro.Shared.Types.ProductType;
using AddNewTradeTrigger = System.Action<CoinbasePro.Shared.Types.ProductType, CoinbaseProToolsForm.NewTradesTriggerState>;
using AddSLUpdateTrigger = System.Action<CoinbasePro.Shared.Types.ProductType, CoinbaseProToolsForm.SLUpdateTriggerState>;
using RemoveNewTradeTrigger = System.Action<CoinbasePro.Shared.Types.ProductType,
	CoinbaseProToolsForm.NewTradesTriggerList,
	int[]>;
using GetProductWideSetting = System.Func<CoinbasePro.Shared.Types.ProductType, bool>;
using ProductWideSetting = System.Tuple<System.Func<CoinbasePro.Shared.Types.ProductType, bool>,
	System.Action<CoinbasePro.Shared.Types.ProductType, bool>>;

namespace CoinbaseProToolsForm
{
	public partial class Form1 : Form
	{
		int currentCommandIndex; // Current index of the console command read input

		CoinbaseProClient cbClient;
		State state;
		Exception previousException;
		Func<Form1> GetTheActiveForm;
		ExceptionFileWriter exceptionFileWriter;
		Func<ProductStatsDictionary> getProductStatsInTheBkg;
		Action<Exception> HandleExceptions;

		Func<ProductType> getActiveProduct;
		Func<ProductType, bool> setActiveProduct;

		Func<TradeHistoryState> getTradeHistory;
		AddNewTradeTrigger addNewTradeTrigger;
		RemoveNewTradeTrigger removeNewTradeTrigger;
		Dictionary<ProductType, NewTradesTriggerList> newTradesTriggers;
		Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers;

		ProductWideSetting rapidPriceChangeUpSetting;
		ProductWideSetting rapidPriceChangeDownSetting;
		ProductWideSetting speechSetting;
		ProductWideSetting rapidLargeBuySetting;
		ProductWideSetting rapidLargeSellSetting;

		public ExceptionUIWriter exceptionUIWriter
		{
			get;
			private set;
		}
		public EventOutputter eventOutputter
		{
			get;
			private set;
		}

		delegate void ThreadSafeCallDelegate(Form1 form, IEnumerable<EventOutput> textArray,ref DateTimeOffset? lastEvent);

		public Form1(State state, CoinbaseProClient cbClient,
			Func<Form1> GetTheActiveForm, Exception previousException,
			ExceptionFileWriter exceptionFileWriter, Func<ProductStatsDictionary> getProductStatsInTheBkg,
			Action<Exception> HandleExceptions,
			Func<TradeHistoryState> getTradeHistory,
			Func<ProductType> getActiveProduct, Func<ProductType, bool> setActiveProduct,
			AddNewTradeTrigger addNewTradeTrigger, RemoveNewTradeTrigger removeNewTradeTrigger,
			Dictionary<ProductType,NewTradesTriggerList> newTradesTriggers,
			Dictionary<ProductType, SLUpdateTriggerList> slUpdateTriggers,
			ProductWideSetting rapidPriceChangeUpSetting,
			ProductWideSetting rapidPriceChangeDownSetting,
			ProductWideSetting speechSetting,
			ProductWideSetting largeBuySetting,
			ProductWideSetting largeSellSetting)
		{
			this.state = state;
			this.cbClient = cbClient;
			this.GetTheActiveForm = GetTheActiveForm;
			this.previousException = previousException;
			this.exceptionFileWriter = exceptionFileWriter;
			this.eventOutputter = CreateEventOutputter(GetTheActiveForm);
			this.exceptionUIWriter = CreateUIExceptionWriter(this.eventOutputter);
			this.getProductStatsInTheBkg = getProductStatsInTheBkg;
			this.HandleExceptions = HandleExceptions;
			this.getTradeHistory = getTradeHistory;
			this.getActiveProduct = getActiveProduct;
			this.setActiveProduct = setActiveProduct;
			this.addNewTradeTrigger = addNewTradeTrigger;
			this.removeNewTradeTrigger = removeNewTradeTrigger;
			this.newTradesTriggers = newTradesTriggers;
			this.slUpdateTriggers = slUpdateTriggers;
			this.rapidPriceChangeUpSetting=rapidPriceChangeUpSetting;
			this.rapidPriceChangeDownSetting = rapidPriceChangeDownSetting;
			this.speechSetting = speechSetting;
			this.rapidLargeBuySetting = largeBuySetting;
			this.rapidLargeSellSetting = largeSellSetting;

			InitializeComponent();
		}

		private static ExceptionUIWriter CreateUIExceptionWriter(EventOutputter eventOutputter)
		{
			// Ignore duplicate strings for 15 seconds
			var ignoreDuplicateStrings = Library.IgnoreDuplicates<string>(Library.StringCompareNoCase, 15);

			return (text) =>
			{
				try
				{
					if (ignoreDuplicateStrings(text))
					{
						eventOutputter(new EventOutput[] { new EventOutput(text, null) });
					}
				}
#pragma warning disable 168
				catch (Exception e)
#pragma warning restore 168
				{
				}
			};
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			InitConsoleWindow();
			InitEventWindow();
		}

		private void InitConsoleWindow()
		{
			OutputToConsole("> ", false);
		}

		private void InitEventWindow()
		{
			if (this.previousException != null)
			{
				var nl = Environment.NewLine;
				this.eventOutputter(new EventOutput[]{ new EventOutput($"Automatically recovered from an exception:{nl}\t" +
					$"{this.previousException.ToString()}",null) });
			}
		}

		private static void _OutputEventThreadSafe(Form1 form, IEnumerable<EventOutput> textArray,
			ref DateTimeOffset? lastEvent)
		{
			try
			{
				if (form != null && !form.eventWindow.IsDisposed || !form.eventWindow.IsHandleCreated)
				{
					if (form.eventWindow.InvokeRequired)
					{
						var d = new ThreadSafeCallDelegate(_OutputEventThreadSafe);
						var args = new object[] { form, textArray, lastEvent /* Idx 2 */};
						form.eventWindow.Invoke(d, args);
						lastEvent = (DateTimeOffset?)args[2];
					}
					else
					{
						if (textArray == null || textArray.Count()==0)
						{
							form.eventWindow.Text = "";
						}
						else
						{
							var builder = new StringBuilder();
							var nl = Environment.NewLine;
							var now = DateTime.Now;
							string nowFormatted = null;
							DateTimeOffset latestTsOfEvent = now;

							foreach (var line in textArray)
							{
								if (line.Item1 != null && line.Item1 != String.Empty)
								{

									string tsFormatted;
									if (line.Item2 == null)
									{
										latestTsOfEvent = now;
										if (nowFormatted == null) nowFormatted = now.ToString("HH:mm");
										tsFormatted = nowFormatted;
									}
									else
									{
										latestTsOfEvent = line.Item2.Value;
										tsFormatted = latestTsOfEvent.ToString("HH:mm");
									}

									if (!lastEvent.HasValue || lastEvent.Value.Day != latestTsOfEvent.Day)
									{
										var conditionalNl = ((form.eventWindow.Text.Length == 0) ? "" : nl);
										builder.Append($"{conditionalNl}{latestTsOfEvent.ToString("dddd dd MMMM")}{nl}{nl}");
									}

									builder.Append($"{tsFormatted} {line.Item1}{nl}");

									lastEvent = latestTsOfEvent;
								}
							}

							form.eventWindow.Text += builder.ToString();

							form.eventWindow.SelectionStart = form.eventWindow.Text.Length;
							form.eventWindow.ScrollToCaret();
						}
					}
				}
			}
#pragma warning disable 168
			catch (Exception e)
#pragma warning restore 168
			{
			}
		}

		private static EventOutputter CreateEventOutputter(Func<Form1> GetTheActiveForm)
		{
			DateTimeOffset? lastEvent = null;

			return (textArray) =>
			{

				var form = GetTheActiveForm();
				_OutputEventThreadSafe(form, textArray, ref lastEvent);
			};
		}

		private async void console_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter)
			{
				// Stops the return from taking effect
				e.Handled = true;

				// Enter must be pressed at the end of the command.
				if (console.SelectionStart == console.TextLength)
				{
					string cmdLine = console.Text.Substring(this.currentCommandIndex,
						console.TextLength - this.currentCommandIndex);
					OutputToConsole(null, true);

					LockConsole();
					await ParseInput(cmdLine);
				}
			}
		}

		private void LockConsole()
		{
			this.console.ReadOnly = true;
		}

		private void UnlockConsole()
		{
			this.console.ReadOnly = false;
		}

		private void CompleteOutputToConsole()
		{
			UnlockConsole();
			OutputToConsole($"{Environment.NewLine}> ", false);
		}

		private void OutputToConsole(string text, bool newLine = true)
		{
			if (text != null && text != String.Empty)
			{
				console.Text += text;
			}
			if (newLine)
			{
				console.Text += Environment.NewLine;
			}
			console.SelectionStart = console.Text.Length;
			console.ScrollToCaret();
			this.currentCommandIndex = console.SelectionStart;
		}

		private async Task ParseInput(string line)
		{

			//sidtodo do a help cmd
			if (line == null || line == "")
			{
			}
			else
			{
				var cmdSplit = line.Split(' ');

				IEnumerable<string> output = null;

				try
				{

					if (StringCompareNoCase(cmdSplit[0], "ACC"))
					{
						output = await Account.AccountCmd(cbClient, cmdSplit, exceptionFileWriter, exceptionUIWriter);
					}
					else if (StringCompareNoCase(cmdSplit[0], "OB")) // order book
					{
						output = OrderBook.OrderBookCmd(cbClient, cmdSplit,
							new GetSetOrderBookState(() => state.orderBookState, (upd) => state.orderBookState = upd),
							eventOutputter, exceptionUIWriter, exceptionFileWriter);
					}
					else if (StringCompareNoCase(cmdSplit[0], "TW")) // Trade watch
					{
						output = await TradeWatch.CmdLine(cbClient, cmdSplit, eventOutputter,
							this.getProductStatsInTheBkg,
							this.HandleExceptions,
							this.getTradeHistory(),
							this.newTradesTriggers,
							this.getActiveProduct,
							this.slUpdateTriggers);
					}
					else if (StringCompareNoCase(cmdSplit[0], "ON"))
					{
						output = Options.CmdLine(this.state.options, cmdSplit, true,
							this.rapidPriceChangeUpSetting, this.rapidPriceChangeDownSetting,
							this.getActiveProduct, this.speechSetting,
							this.rapidLargeBuySetting,this.rapidLargeSellSetting);
					}
					else if (StringCompareNoCase(cmdSplit[0], "OFF"))
					{
						output = Options.CmdLine(this.state.options, cmdSplit, false,
							this.rapidPriceChangeUpSetting, this.rapidPriceChangeDownSetting,
							this.getActiveProduct, this.speechSetting,
							this.rapidLargeBuySetting, this.rapidLargeSellSetting);
					}
					else if (StringCompareNoCase(cmdSplit[0], "SHOW"))
					{
						output = Options.CmdLine(this.state.options, cmdSplit, null,
							this.rapidPriceChangeUpSetting, this.rapidPriceChangeDownSetting,
							this.getActiveProduct, this.speechSetting,
							this.rapidLargeBuySetting, this.rapidLargeSellSetting);
					}
					else if (StringCompareNoCase(cmdSplit[0], "PROD"))
					{
						output = Products.CmdLine(cmdSplit, this.setActiveProduct);
					}
					else if (StringCompareNoCase(cmdSplit[0], "TRIGGER"))
					{
						output = Trigger.CmdLine(cmdSplit, this.getActiveProduct,null /* //sidtodo */, addNewTradeTrigger,
							removeNewTradeTrigger, newTradesTriggers);
					}
					else
					{
						output = new string[] { "Unknown command" };
					}
				}
				catch (Exception e)
				{
					output=new string[] { e.ToString() };
				}

				if (output!=null)
				{
					var outputBuilder = new StringBuilder();
					foreach(var outputLine in output)
					{
						outputBuilder.AppendLine(outputLine);
					}

					OutputToConsole(outputBuilder.ToString(), true);
				}
			}

			CompleteOutputToConsole();
		}

		//SIDTODO FIX THIS.

		//private void console_SelectionChanged(object sender, EventArgs e)
		//{
		//	if (console.SelectionStart < this.currentCommandIndex)
		//	{
		//		//sidtodo fix this all of a sudden not working???
		//		console.SelectionStart = this.currentCommandIndex;
		//		console.ScrollToCaret();
		//	}
		//}
	}
}
