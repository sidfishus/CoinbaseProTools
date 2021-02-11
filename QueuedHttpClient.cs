using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IHttpClient = CoinbasePro.Network.HttpClient.IHttpClient;
using HttpClient = CoinbasePro.Network.HttpClient.HttpClient;
using System.Net.Http;
using System.Threading;

namespace CoinbaseProToolsForm
{
	public class QueuedHttpClient : IHttpClient
	{
		HttpClient client;
		IBurstQueueMethods<HttpResponseMessage> queue;
		

		public QueuedHttpClient()
		{
			this.client = new HttpClient();
			this.queue = BurstQueue.Create<HttpResponseMessage>(1000, 3);
			this.queue.Start();
		}

		public Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
		{
			throw new NotImplementedException();
		}

		public Task<HttpResponseMessage> SendAsync(HttpRequestMessage httpRequestMessage)
		{
			//// Queue is the way to go.

			////int requestIdx = -1;
			////lock (theLock)
			////{
			////	//sidtodo check the last send
			////	requestMessageQueue.Add(httpRequestMessage);
			////	requestIdx = requestMessageQueue.Count-1;
			////	ConditionalStartTheSendThread();
			////}
			//lock (theLock)
			//{
			//	var now = DateTime.Now;

			//}


			return this.queue.Add(() => ((IHttpClient)this.client).SendAsync(httpRequestMessage));
		}

		public Task<string> ReadAsStringAsync(HttpResponseMessage httpRequestMessage)
		{
			return ((IHttpClient)this.client).ReadAsStringAsync(httpRequestMessage);
		}
	}
}
