using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskAndHistory = System.Tuple<System.Threading.Tasks.Task, System.DateTimeOffset>;

namespace CoinbaseProToolsForm
{
	public interface IBurstQueueMethods<T>
	{
		Task<T> Add(Func<Task<T>> action);
		Task Start();
	}

	class QueueState<T>
	{
		public object theQueueLock = new object();
		public System.Collections.Generic.List<System.Tuple<Func<Task<T>>, System.Action<T, System.Exception>>> theQueue=
			new System.Collections.Generic.List<System.Tuple<Func<Task<T>>, System.Action<T, System.Exception>>>();
		public TaskSignaler workToDoSignaler=new TaskSignaler();
		public readonly int maxPerDuration;
		public readonly int durationMs;

		public QueueState(int durationMs, int maxPerDuration)
		{
			this.durationMs = durationMs;
			this.maxPerDuration = maxPerDuration;
		}
	}

	class CompareTaskAndHistoryByTime : IComparer<TaskAndHistory>
	{
		int IComparer<TaskAndHistory>.Compare(TaskAndHistory lhs, TaskAndHistory rhs)
		{
			return (int)(lhs.Item2 - rhs.Item2).TotalMilliseconds;
		}
	}

	public static class BurstQueue
	{
		class BurstQueueInstance<T> : IBurstQueueMethods<T>
		{
			public Func<Func<Task<T>>, Task<T>> Add;
			public Func<Task> Start;
			Task<T> IBurstQueueMethods<T>.Add(Func<Task<T>> action)
			{
				return this.Add(action);
			}

			Task IBurstQueueMethods<T>.Start()
			{
				return this.Start();
			}
		}

		private static async Task ProcessQueueItemAndCallback<T>(
			System.Tuple<Func<Task<T>>, System.Action<T, System.Exception>> queueItem,
			TaskAndHistory[] taskHistory,int taskHistoryIdx)
		{
			try
			{
				T res = await queueItem.Item1();
				//Console.WriteLine($"Task idx {thisTaskHistoryArrayIdx} {taskHistory[thisTaskHistoryArrayIdx].Item1.Id} " +
				//	$"finished: {DebugFormatDateTime(new DateTimeOffset(DateTime.Now))}");

				// Do the callback
				queueItem.Item2(res, null);
			}
			catch (Exception e)
			{
				// Do the callback but with an exception
				queueItem.Item2(default(T), e);
			}

			// Store the time that the task completed
			taskHistory[taskHistoryIdx] =
				new TaskAndHistory(taskHistory[taskHistoryIdx].Item1, DateTime.Now);
		}

		private static async Task ProcessQueueAsync<T>(QueueState<T> queueState)
		{
			try
			{

				var taskHistory = new TaskAndHistory[queueState.maxPerDuration];
				int taskHistoryTopIdx = -1;

				do
				{

					System.Collections.Generic.List<System.Tuple<Func<Task<T>>, System.Action<T, System.Exception>>>
						itemsToProcess = null;
					lock (queueState.theQueueLock)
					{

						if (queueState.theQueue.Count > 0)
						{
							queueState.workToDoSignaler.Reset();
							itemsToProcess = queueState.theQueue;
							queueState.theQueue = new
								System.Collections.Generic.List<System.Tuple<Func<Task<T>>, System.Action<T, System.Exception>>>();
						}
					}

					if (itemsToProcess != null)
					{

						// The index of the next item to process
						int nextItemToProcessIdx = 0;

						do
						{
							var now = DateTimeOffset.Now;
							//Console.WriteLine($"Coming around again {nextItemToProcessIdx}, {itemsToProcess.Count}, "+
							//	$"taskHistoryTopIdx={taskHistoryTopIdx}, now is {DebugFormatDateTime(now)}.");

							int msBetweenNowAndBeginningOfLatestBatch = 0;

							// Clear the tasks from the history which are part of the previous batch and no longer relevant
							if (taskHistoryTopIdx >= 0)
							{
								bool isMoreThanOneItem = (taskHistoryTopIdx > 0);
								if (isMoreThanOneItem)
								{
									// Order the history by time order ascending
									Array.Sort(taskHistory, 0, taskHistoryTopIdx + 1, new CompareTaskAndHistoryByTime());

									// Iterate recent tasks
									for (int i = taskHistoryTopIdx; i >= 1; --i)
									{
										// Is there a difference of more than <duration> between this and the previous task?
										var newerTask = taskHistory[i].Item2;
										var olderTask = taskHistory[i - 1].Item2;
										if ((newerTask - olderTask).TotalMilliseconds >= queueState.durationMs)
										{
											// Yes
											// Remove the older one(s)

											int newTop = -1;
											for (int ii = 0; ii <= taskHistoryTopIdx; ++ii)
											{
												if (i <= taskHistoryTopIdx)
												{
													taskHistory[ii] = taskHistory[i];
													++i;
												}
												else
												{
													if (newTop == -1) newTop = ii - 1;
													taskHistory[ii] = null;
												}
											}
											taskHistoryTopIdx = newTop;
											break;
										}
									}
								}

								// Has there been more than <duration> between now and the oldest task? I.e. the beginning of the
								// latest batch
								var earliestTask = taskHistory[0];
								msBetweenNowAndBeginningOfLatestBatch = (int)(now - earliestTask.Item2).TotalMilliseconds;
								if (msBetweenNowAndBeginningOfLatestBatch < queueState.durationMs)
								{
									// No
									//Console.WriteLine($"Task idx {0} {taskHistory[0].Item1.Id} is " +
									//	$"{(int)(now - earliestTask.Item2).TotalMilliseconds} MS old. " +
									//	$"Task time={DebugFormatDateTime(earliestTask.Item2)}");
								}
								else
								{
									// Clear the task history, none of it is relevant any more.
									taskHistoryTopIdx = -1;
									for (int i = 0; i < taskHistory.Length; ++i) taskHistory[i] = null;
								}
							}

							int numRequestsInLatestBatch = taskHistoryTopIdx + 1;
							if (numRequestsInLatestBatch == queueState.maxPerDuration)
							{
								// Wait for <duration> minus the difference between now and the earliest task.
								int delayMs = queueState.durationMs - msBetweenNowAndBeginningOfLatestBatch;

								// Let the tasks complete, and come around again.
								//Console.WriteLine($"waiting: {delayMs}. now={DebugFormatDateTime(now)}");
								await Task.Delay(delayMs);
							}
							else
							{

								// Batch new tasks
								int numNewTasks = Math.Min(queueState.maxPerDuration - numRequestsInLatestBatch,
									itemsToProcess.Count - nextItemToProcessIdx);
								Task[] taskToWaitArray = new Task[numNewTasks];
								//Console.WriteLine($"numNewTasks = {numNewTasks}");

								for (int taskToWaitIdx = 0; taskToWaitIdx < numNewTasks; ++taskToWaitIdx)
								{
									var item = itemsToProcess[nextItemToProcessIdx];
									//Console.WriteLine($"processing {nextItemToProcessIdx}");
									++nextItemToProcessIdx;
									var thisTaskHistoryArrayIdx = ++taskHistoryTopIdx;

									var taskForItem = ProcessQueueItemAndCallback(item, taskHistory, thisTaskHistoryArrayIdx);
									taskHistory[thisTaskHistoryArrayIdx] = new TaskAndHistory(taskForItem,
										now /* A dummy value that will be updated after the task */);

									taskToWaitArray[taskToWaitIdx] = taskForItem;
								}

								//Console.WriteLine($"itemProcessedIdx={nextItemToProcessIdx}");

								await Task.WhenAll(taskToWaitArray);
							}
						} while (nextItemToProcessIdx < itemsToProcess.Count);
					}

					// Wait till we are signaled there is work
					//Console.WriteLine("Waiting for more work.");
					await queueState.workToDoSignaler.AWait();
				} while (true);
			}
#pragma warning disable 168
			catch (Exception e)
#pragma warning restore 168
			{
				//sidtodo write exception
#pragma warning disable 4014
				ProcessQueueAsync(queueState);
#pragma warning restore 4014
			}
		}

		public static IBurstQueueMethods<T> Create<T>(int durationMs, int maxPerDuration)
		{
			var bqi = new BurstQueueInstance<T>();
			var state = new QueueState<T>(durationMs, maxPerDuration);

			bqi.Add = (action) =>
			{
				var tcs = new TaskCompletionSource<T>();

				Action<T, Exception> callbackWhenDone = (theResult, exception) =>
				{
					if (exception != null)
					{
						tcs.SetException(exception);
					}
					else
					{
						tcs.SetResult(theResult);
					}
				};

				lock (state.theQueueLock)
				{
					state.theQueue.Add(new System.Tuple<Func<Task<T>>, System.Action<T, System.Exception>>(action, callbackWhenDone));
					state.workToDoSignaler.Signal();
				}

				return tcs.Task;
			};

			bqi.Start = () => ProcessQueueAsync(state);

			return bqi;
		}
	}
}
