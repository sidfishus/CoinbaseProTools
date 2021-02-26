using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoinbaseProToolsForm
{
	public class LockedByRef<T>
	{
		public readonly TimedLock theLock = new TimedLock();
		public T Ref { get; set; }
	}
}
