using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;

namespace WalletWasabi.Bases
{
	public abstract class PeriodicRunner : BackgroundService
	{
		protected PeriodicRunner(TimeSpan period)
		{
			Trigger = new SemaphoreSlim(0, 1);
			Period = period;
			ResetLastException();
		}

		private SemaphoreSlim Trigger { get; set; }
		public TimeSpan Period { get; }
		public Exception LastException { get; set; }
		public long LastExceptionCount { get; set; }
		public DateTimeOffset LastExceptionFirstAppeared { get; set; }

		private void ResetLastException()
		{
			LastException = null;
			LastExceptionCount = 0;
			LastExceptionFirstAppeared = DateTimeOffset.MinValue;
		}

		public void TriggerRound()
		{
			if (Trigger.CurrentCount == 0)
			{
				Trigger.Release();
			}
		}

		protected abstract Task ActionAsync(CancellationToken cancel);

		/// <inheritdoc />
		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await ActionAsync(stoppingToken).ConfigureAwait(false);
					LogAndResetLastExceptionIfNotNull();
				}
				catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException || ex is TimeoutException)
				{
					Logger.LogTrace(ex);
				}
				catch (Exception ex)
				{
					// Only log one type of exception once.
					if (LastException is null // If the exception never came.
						|| ex.GetType() != LastException.GetType() // Or the exception have different type from previous exception.
						|| ex.Message != LastException.Message) // Or the exception have different message from previous exception.
					{
						// Then log and reset the last exception if another one came before.
						LogAndResetLastExceptionIfNotNull();
						// Set new exception and log it.
						LastException = ex;
						LastExceptionFirstAppeared = DateTimeOffset.UtcNow;
						LastExceptionCount = 1;
						Logger.LogError(ex);
					}
					else
					{
						// Increment the exception counter.
						LastExceptionCount++;
					}
				}
				finally
				{
					try
					{
						await Trigger.WaitAsync(Period, stoppingToken).ConfigureAwait(false);
					}
					catch (TaskCanceledException ex)
					{
						Logger.LogTrace(ex);
					}
				}
			}
		}

		private void LogAndResetLastExceptionIfNotNull()
		{
			if (LastException != null)
			{
				Logger.LogInfo($"Exception stopped coming. It came for {(DateTimeOffset.UtcNow - LastExceptionFirstAppeared).TotalSeconds} seconds, {LastExceptionCount} times: {LastException.ToTypeMessageString()}");
				ResetLastException();
			}
		}

		public override void Dispose()
		{
			Trigger?.Dispose();
			Trigger = null;

			base.Dispose();
		}
	}
}
