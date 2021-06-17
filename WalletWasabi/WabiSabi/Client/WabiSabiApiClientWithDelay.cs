using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Models;

namespace WalletWasabi.WabiSabi.Client
{
	public class WabiSabiApiClientWithDelay : IWabiSabiApiRequestHandler
	{
		private static Random Random { get; } = new();
		private ConcurrentStack<DateTimeOffset> InputRegistrationSchedule { get; }
		private ConcurrentStack<DateTimeOffset> SignatureRequestsSchedule { get; }

		public WabiSabiApiClientWithDelay(IWabiSabiApiRequestHandler innerClient,
			int expectedNumberOfInputs,
			TimeSpan inputRegistrationTimeFrame,
			TimeSpan transactionSigningTimeFrame)
		{
			InnerClient = innerClient;
			InputRegistrationSchedule = CreateSchedule(DateTimeOffset.Now, inputRegistrationTimeFrame, expectedNumberOfInputs);
			SignatureRequestsSchedule = CreateSchedule(DateTimeOffset.Now, transactionSigningTimeFrame, expectedNumberOfInputs);
		}

		public IWabiSabiApiRequestHandler InnerClient { get; }

		public async Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request, CancellationToken cancellationToken)
		{
			await DelayAccordingToScheduleAsync(InputRegistrationSchedule, cancellationToken).ConfigureAwait(false);
			return await InnerClient.RegisterInputAsync(request, cancellationToken).ConfigureAwait(false);
		}

		public async Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request, CancellationToken cancellationToken)
		{
			return await InnerClient.ConfirmConnectionAsync(request, cancellationToken).ConfigureAwait(false);
		}

		public async Task<ReissueCredentialResponse> ReissueCredentialAsync(ReissueCredentialRequest request, CancellationToken cancellationToken)
		{
			return await InnerClient.ReissueCredentialAsync(request, cancellationToken).ConfigureAwait(false);
		}

		public async Task<OutputRegistrationResponse> RegisterOutputAsync(OutputRegistrationRequest request, CancellationToken cancellationToken)
		{
			return await InnerClient.RegisterOutputAsync(request, cancellationToken);
		}

		public async Task RemoveInputAsync(InputsRemovalRequest request, CancellationToken cancellationToken)
		{
			await InnerClient.RemoveInputAsync(request, cancellationToken).ConfigureAwait(false);
		}

		public async Task SignTransactionAsync(TransactionSignaturesRequest request, CancellationToken cancellationToken)
		{
			await DelayAccordingToScheduleAsync(SignatureRequestsSchedule, cancellationToken).ConfigureAwait(false);
			await InnerClient.SignTransactionAsync(request, cancellationToken).ConfigureAwait(false);
		}

		public async Task<RoundState[]> GetStatusAsync(CancellationToken cancellationToken)
		{
			return await InnerClient.GetStatusAsync(cancellationToken).ConfigureAwait(false);
		}

		private async Task DelayAccordingToScheduleAsync(ConcurrentStack<DateTimeOffset> schedule, CancellationToken cancellationToken)
		{
			if (schedule.TryPop(out var scheduledExecutionTime))
			{
				var timeToWait = scheduledExecutionTime - DateTimeOffset.Now;
				var fixedTimeToWait = timeToWait < TimeSpan.Zero ? TimeSpan.Zero : timeToWait;

				await Task.Delay(fixedTimeToWait, cancellationToken).ConfigureAwait(false);
				return;
			}

			throw new InvalidOperationException("All scheduled requests have already been made.");
		}

		private ConcurrentStack<DateTimeOffset> CreateSchedule(DateTimeOffset startTime, TimeSpan timeFrame, int expectedNumberOfInputs)
		{
			var timeFrameSamples = Enumerable
				.Range(0, expectedNumberOfInputs)
				.Select(_ => startTime.Add(Random.NextDouble() * timeFrame))
				.ToList();
			timeFrameSamples.Shuffle();
			return new ConcurrentStack<DateTimeOffset>(timeFrameSamples);
		}
	}
}
