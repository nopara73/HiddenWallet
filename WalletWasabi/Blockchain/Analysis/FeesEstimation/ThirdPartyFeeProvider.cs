using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Backend.Models.Responses;
using WalletWasabi.Bases;
using WalletWasabi.Models;
using WalletWasabi.Nito.AsyncEx;
using WalletWasabi.Services;
using WalletWasabi.WebClients.BlockstreamInfo;

namespace WalletWasabi.Blockchain.Analysis.FeesEstimation
{
	public class ThirdPartyFeeProvider : PeriodicRunner
	{
		public ThirdPartyFeeProvider(TimeSpan period, WasabiSynchronizer synchronizer, BlockstreamInfoFreeProvider blockstreamProvider)
			: base(period)
		{
			Synchronizer = synchronizer;
			BlockstreamProvider = blockstreamProvider;
		}

		public event EventHandler<AllFeeEstimate>? AllFeeEstimateArrived;

		public WasabiSynchronizer Synchronizer { get; }
		public BlockstreamInfoFreeProvider BlockstreamProvider { get; }
		public AllFeeEstimate? LastAllFeeEstimate { get; private set; }
		private object Lock { get; } = new object();
		public bool InError { get; private set; }
		private AbandonedTasks ProcessingEvents { get; } = new();

		public override async Task StartAsync(CancellationToken cancellationToken)
		{
			SetAllFeeEstimateIfLooksBetter(Synchronizer.LastResponse?.AllFeeEstimate);
			SetAllFeeEstimateIfLooksBetter(BlockstreamProvider.LastAllFeeEstimate);

			Synchronizer.ResponseArrived += Synchronizer_ResponseArrived;
			BlockstreamProvider.AllFeeEstimateArrived += OnAllFeeEstimateArrived;

			await base.StartAsync(cancellationToken).ConfigureAwait(false);
		}

		public override async Task StopAsync(CancellationToken cancellationToken)
		{
			Synchronizer.ResponseArrived -= Synchronizer_ResponseArrived;
			BlockstreamProvider.AllFeeEstimateArrived -= OnAllFeeEstimateArrived;

			await ProcessingEvents.WhenAllAsync().ConfigureAwait(false);
			await base.StopAsync(cancellationToken).ConfigureAwait(false);
		}

		private void Synchronizer_ResponseArrived(object? sender, SynchronizeResponse e)
		{
			OnAllFeeEstimateArrived(sender, e.AllFeeEstimate);
		}

		private void OnAllFeeEstimateArrived(object? sender, AllFeeEstimate? fees)
		{
			using (RunningTasks.RememberWith(ProcessingEvents))
			{
				// Only go further if we have estimations.
				if (fees?.Estimations?.Any() is not true)
				{
					return;
				}

				var notify = false;
				lock (Lock)
				{
					notify = SetAllFeeEstimate(fees);
				}

				if (notify)
				{
					AllFeeEstimateArrived?.Invoke(this, fees);
				}
			}
		}

		private bool SetAllFeeEstimateIfLooksBetter(AllFeeEstimate? fees)
		{
			var current = LastAllFeeEstimate;
			if (fees is null
				|| fees == current
				|| (current is not null && ((!fees.IsAccurate && current.IsAccurate) || fees.Estimations.Count <= current.Estimations.Count)))
			{
				return false;
			}
			return SetAllFeeEstimate(fees);
		}

		/// <returns>True if changed.</returns>
		private bool SetAllFeeEstimate(AllFeeEstimate fees)
		{
			if (LastAllFeeEstimate == fees)
			{
				return false;
			}
			LastAllFeeEstimate = fees;
			return true;
		}

		protected override Task ActionAsync(CancellationToken cancel)
		{
			if (Synchronizer.BackendStatus == BackendStatus.Connected)
			{
				BlockstreamProvider.IsPaused = true;
				InError = false;
			}
			else
			{
				BlockstreamProvider.IsPaused = false;
				InError = BlockstreamProvider.InError;
			}

			return Task.CompletedTask;
		}
	}
}