using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoinbaseProToolsForm
{
	struct PerformAtEndOfScope : IDisposable
	{
		Action theAction;
		public PerformAtEndOfScope(Action action)
		{
			this.theAction = action;
		}

		void IDisposable.Dispose()
		{
			theAction();
		}
	}
}
