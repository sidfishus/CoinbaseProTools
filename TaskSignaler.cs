using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoinbaseProToolsForm
{
	public class TaskSignaler
	{
		// The int type is because I don't have the non generic version of TaskCompletionSource.
		TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();

		public void Signal()
		{
			if (!this.tcs.Task.IsCompleted)
			{
				this.tcs.SetResult(0);
			}
		}

		public void Reset()
		{
			this.tcs = new TaskCompletionSource<int>();
		}

		public Task AWait()
		{
			return this.tcs.Task;
		}
	}
}
