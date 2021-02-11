using System;
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
	}
}
