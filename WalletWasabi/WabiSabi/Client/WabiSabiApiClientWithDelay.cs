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
		public WabiSabiApiClientWithDelay(
			IWabiSabiApiRequestHandler innerClient,
			int expectedNumberOfInputs,
			Task<TimeSpan> inputRegistrationPhaseReady,
			Task<TimeSpan> signingPhaseReady)
		{
			InnerClient = innerClient;
			ExpectedNumberOfInputs = expectedNumberOfInputs;
			InputRegistrationPhaseReady = inputRegistrationPhaseReady;
			SigningPhaseReady = signingPhaseReady;
		}

		public IWabiSabiApiRequestHandler InnerClient { get; }
		public int ExpectedNumberOfInputs { get; }
		public Task<TimeSpan> InputRegistrationPhaseReady { get; }
		public Task<TimeSpan> SigningPhaseReady { get; }
		private static Random Random { get; } = new();
		private ConcurrentStack<DateTimeOffset>? InputRegistrationSchedule { get; set; }
		private ConcurrentStack<DateTimeOffset>? SignatureRequestsSchedule { get; set; }

		public async Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request, CancellationToken cancellationToken)
		{
			var inputRegistrationTimeout = await InputRegistrationPhaseReady.ConfigureAwait(false);
			InputRegistrationSchedule ??= CreateSchedule(
				DateTimeOffset.Now,
				inputRegistrationTimeout,
				ExpectedNumberOfInputs);

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
			var transactionSigningTimeout = await SigningPhaseReady.ConfigureAwait(false);
			SignatureRequestsSchedule ??= CreateSchedule(
				DateTimeOffset.Now,
				transactionSigningTimeout,
				ExpectedNumberOfInputs);
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

		private ConcurrentStack<DateTimeOffset> CreateSchedule(DateTimeOffset startTime, TimeSpan timeFrame, int expectedNumberOfInputs) =>
			 new(Enumerable
				.Range(0, expectedNumberOfInputs)
				.Select(_ => startTime.Add(0.8 * Random.NextDouble() * timeFrame)))
				.OrderByAscending(t => t);
	}
}
