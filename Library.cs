using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoinbasePro.Shared.Types;
using ExceptionFileWriter = System.Action<string>;
using ExceptionUIWriter = System.Action<string>;

namespace CoinbaseProToolsForm
{
	public class Library
	{

		public static bool StringCompareNoCase(string lhs, string rhs)
		{
			return String.Equals(lhs, rhs, StringComparison.OrdinalIgnoreCase);
		}

		public static void Test()
		{
			System.Windows.Forms.MessageBox.Show("Test");
		}

		public static void RepeatNTimes(int n, Action action)
		{
			for (int i = 0; i < n; ++i)
			{
				action();
			}
		}

		public static string EnabledText(bool enabled)
		{
			return ((enabled) ? "enabled" : "disabled");
		}

		public static Action<string> SynchronisedFileAppender(string filePath,
			Func<string, string> textFormatter = null,
			Func<string, bool> ignore = null)
		{
			object fileLock = new object();

			return (text) =>
			{
				try
				{
					lock (fileLock)
					{
						if (ignore == null || ignore(text))
						{
							if (textFormatter != null) text = textFormatter(text);

							using (var sw = System.IO.File.AppendText(filePath))
							{
								sw.Write(text);
							}
						}
					}
				}
#pragma warning disable 168
				catch (Exception e)
#pragma warning restore 168
				{
				}
			};
		}

		public static Func<T, bool> IgnoreDuplicates<T>(Func<T,T,bool> comparer,
			int? intervalSecs=null /* Can be used to make it only affect for a certain amount of time */)
		{
			T last = default(T);
			DateTime? lastTime=null;

			return (newThing) =>
			{
				if (comparer(newThing, last))
				{
					if (!intervalSecs.HasValue) return false;

					var now = DateTime.Now;

					if (lastTime.HasValue && (now - lastTime.Value).TotalSeconds < intervalSecs)
					{
						return false;
					}

					lastTime = now;
				}
				else
				{
					if (intervalSecs.HasValue)
					{
						lastTime = DateTime.Now;
					}
				}

				last = newThing;
				return true;
			};
		}

		//sidtodo here check the 24 hour volume. this can give clues as to the trend
		public static Task RunAsyncHandleExceptions(Action<Exception> HandleExceptions, Func<Task> action)
		{
			return Task.Run(async () =>
			{
				try
				{
					await action();
				}
				catch (Exception e)
				{
					HandleExceptions(e);
				}
			});
		}

		public static void AsyncSpeak(string text)
		{
			var synth = new System.Speech.Synthesis.SpeechSynthesizer();

			// Configure the audio output.   
			synth.SetOutputToDefaultAudioDevice();

			var prompt = new System.Speech.Synthesis.Prompt(text);

			// Speak the contents of the prompt asynchronously.  
			synth.SpeakAsync(text);
		}
	}
}
