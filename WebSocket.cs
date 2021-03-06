using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoinbasePro.WebSocket.Models.Response;
using System.Threading;
using CoinbasePro;

namespace CoinbaseProToolsForm
{
	public class WebSocketState
	{
		public EventHandler<WebfeedEventArgs<Snapshot>> onSnapshotReceived;
		public EventHandler<WebfeedEventArgs<Level2>> onLevel2Received;
		public EventHandler<WebfeedEventArgs<SuperSocket.ClientEngine.ErrorEventArgs>> onWebSocketError;
		public readonly TimedLock theLock = new TimedLock();
	}

	public static class WebSocket
	{
		// The client needs to lock
		public static void Stop(WebSocketState state, CoinbaseProClient cbClient)
		{
			var webSocket = cbClient.WebSocket;
			if (webSocket.State == WebSocket4Net.WebSocketState.Connecting ||
				webSocket.State == WebSocket4Net.WebSocketState.Open)
			{
				webSocket.Stop();
			}

			if (state.onSnapshotReceived != null)
			{
				webSocket.OnSnapShotReceived -= state.onSnapshotReceived;
				state.onSnapshotReceived = null;
			}

			if (state.onLevel2Received != null)
			{
				webSocket.OnLevel2UpdateReceived -= state.onLevel2Received;
				state.onLevel2Received = null;
			}

			if (state.onWebSocketError != null)
			{
				webSocket.OnWebSocketError -= state.onWebSocketError;
				state.onWebSocketError = null;
			}
		}
	}
}
