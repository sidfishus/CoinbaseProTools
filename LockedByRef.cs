using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace CoinbaseProToolsForm
{
	public class LockedByRef<T>
	{
		public readonly TimedLock theLock = new TimedLock();
		public T Ref { get; set; }

		public async Task Clear()
		{
			using (await theLock.LockAsync(Timeout.InfiniteTimeSpan))
			{
				Ref = default(T);
			}
		}
	}
}
