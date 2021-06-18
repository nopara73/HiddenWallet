namespace System.Threading.Tasks
{
	public static class TaskExtensions
	{
		/// <summary>
		/// UNSAFE! You are cancelling the wait on the callback of the original Task, not cancelling the operation itself.
		/// https://stackoverflow.com/questions/14524209/what-is-the-correct-way-to-cancel-an-async-operation-that-doesnt-accept-a-cance/14524565#14524565
		/// </summary>
		public static async Task<T> WithAwaitCancellationAsync<T>(this Task<T> me, CancellationToken cancel, int waitForGracefulTerminationMilliseconds = 0)
		{
			// The task completion source.
			var tcs = new TaskCompletionSource<bool>();

			// Register with the cancellation token.
			using (cancel.Register(s => (s as TaskCompletionSource<bool>)?.TrySetResult(true), tcs))
			{
				// If the task waited on is the cancellation token...
				if (me != await Task.WhenAny(me, tcs.Task).ConfigureAwait(false))
				{
					if (waitForGracefulTerminationMilliseconds <= 0)
					{
						throw new OperationCanceledException(cancel);
					}
					else
					{
						using var cts = new CancellationTokenSource(waitForGracefulTerminationMilliseconds);
						return await me.WithAwaitCancellationAsync(cts.Token).ConfigureAwait(false);
					}
				}
			}

			// Wait for one or the other to complete.
			return await me.ConfigureAwait(false);
		}

		/// <summary>
		/// UNSAFE! You are cancelling the wait on the callback of the original Task, not cancelling the operation itself.
		/// https://stackoverflow.com/questions/14524209/what-is-the-correct-way-to-cancel-an-async-operation-that-doesnt-accept-a-cance/14524565#14524565
		/// </summary>
		public static async Task WithAwaitCancellationAsync(this Task me, CancellationToken cancel, int waitForGracefulTerminationMilliseconds = 0)
		{
			// The task completion source.
			var tcs = new TaskCompletionSource<bool>();

			// Register with the cancellation token.
			using (cancel.Register(s => (s as TaskCompletionSource<bool>)?.TrySetResult(true), tcs))
			{
				// If the task waited on is the cancellation token...
				if (me != await Task.WhenAny(me, tcs.Task).ConfigureAwait(false))
				{
					if (waitForGracefulTerminationMilliseconds <= 0)
					{
						throw new OperationCanceledException(cancel);
					}
					else
					{
						using var cts = new CancellationTokenSource(waitForGracefulTerminationMilliseconds);
						await me.WithAwaitCancellationAsync(cts.Token).ConfigureAwait(false);
						return;
					}
				}
			}

			// Wait for one or the other to complete.
			await me.ConfigureAwait(false);
		}

		public static async Task<T> WithAwaitCancellationAsync<T>(this Task<T> me, int millisecondsDelay)
			=> await me.WithAwaitCancellationAsync(ThreadingHelpers.Cancelled, millisecondsDelay).ConfigureAwait(false);

		public static async Task<T> WithAwaitCancellationAsync<T>(this Task<T> me, TimeSpan timeout)
			=> await me.WithAwaitCancellationAsync(ThreadingHelpers.Cancelled, (int)timeout.TotalMilliseconds).ConfigureAwait(false);

		public static async Task WithAwaitCancellationAsync(this Task me, int millisecondsDelay)
			=> await me.WithAwaitCancellationAsync(ThreadingHelpers.Cancelled, millisecondsDelay).ConfigureAwait(false);

		public static async Task WithAwaitCancellationAsync(this Task me, TimeSpan timeout)
			=> await me.WithAwaitCancellationAsync(ThreadingHelpers.Cancelled, (int)timeout.TotalMilliseconds).ConfigureAwait(false);

		public static async Task<TResult> ThenAsync<T, TResult>(this Task<T> me, Func<T, TResult> extractor)
			=> extractor(await me.ConfigureAwait(false));
	}
}
