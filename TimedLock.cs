using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

// Forked from: https://www.rocksolidknowledge.com/articles/locking-and-asyncawait
// Get around the fact you can't use await within a lock statement.
namespace CoinbaseProToolsForm
{
	public class TimedLock
	{
		private readonly SemaphoreSlim toLock;

		public TimedLock()
		{
			toLock = new SemaphoreSlim(1, 1);
		}

		public async Task<TimedLockResult> LockAsync(TimeSpan timeout)
		{
			if (await toLock.WaitAsync(timeout))
			{
				return new TimedLockResult(toLock);
			}
			return new TimedLockResult(null);
		}

		public struct TimedLockResult : IDisposable
		{
			private readonly SemaphoreSlim toRelease;

			public TimedLockResult(SemaphoreSlim toRelease)
			{
				this.toRelease = toRelease;
			}
			public void Dispose()
			{
				if (toRelease != null)
				{
					toRelease.Release();
				}
			}

			public bool Locked
			{
				get
				{
					return toRelease != null;
				}
			}
		}
	}
}