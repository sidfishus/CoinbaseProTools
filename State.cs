using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Types=CoinbasePro.Shared.Types;

namespace CoinbaseProToolsForm
{
	public class State
	{

		public OrderBookState orderBookState=null;

		public ProductsState productsState = null;

		public OptionsState options;
	}
}
