using NBitcoin;
using NBitcoin.RPC;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Crypto;
using WalletWasabi.Crypto.Randomness;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.Models;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Crypto.CredentialRequesting;
using WalletWasabi.WabiSabi.Models;
using WalletWasabi.WabiSabi.Models.MultipartyTransaction;

namespace WalletWasabi.WabiSabi.Backend.Rounds
{
	public class Arena : PeriodicRunner
	{
		public Arena(TimeSpan period, Network network, WabiSabiConfig config, IRPCClient rpc, Prison prison) : base(period)
		{
			Network = network;
			Config = config;
			Rpc = rpc;
			Prison = prison;
			Random = new SecureRandom();
		}

		public ConcurrentDictionary<OutPoint, Alice> AlicesByOutpoint { get; } = new();
		public ConcurrentDictionary<uint256, Alice> AlicesById { get; } = new();
		public ConcurrentDictionary<uint256, Round> RoundsById { get; } = new();

		public HashSet<Round> Rounds { get; } = new();
		private AsyncLock AsyncLock { get; } = new();
		public Network Network { get; }
		public WabiSabiConfig Config { get; }
		public IRPCClient Rpc { get; }
		public Prison Prison { get; }
		public SecureRandom Random { get; }

		public IEnumerable<Round> ActiveRounds => Rounds.Where(x => x.Phase != Phase.Ended);

		private void RemoveRound(Round round)
		{
			foreach (var alice in round.Alices)
			{
				AlicesById.Remove(alice.Id, out _);
				AlicesByOutpoint.Remove(alice.Coin.Outpoint, out _);
			}

			RoundsById.Remove(round.Id, out _);
			Rounds.Remove(round);
		}

		protected override async Task ActionAsync(CancellationToken cancel)
		{
			using (await AsyncLock.LockAsync(cancel).ConfigureAwait(false))
			{
				TimeoutRounds();

				TimeoutAlices();

				await StepTransactionSigningPhaseAsync().ConfigureAwait(false);

				StepOutputRegistrationPhase();

				StepConnectionConfirmationPhase();

				StepInputRegistrationPhase();

				cancel.ThrowIfCancellationRequested();

				// Ensure there's at least one non-blame round in input registration.
				await CreateRoundsAsync().ConfigureAwait(false);
			}
		}

		private void StepInputRegistrationPhase()
		{
			foreach (var round in Rounds.Where(x =>
				x.Phase == Phase.InputRegistration
				&& x.IsInputRegistrationEnded(Config.MaxInputCountByRound, Config.GetInputRegistrationTimeout(x)))
				.ToArray())
			{
				if (round.InputCount < Config.MinInputCountByRound)
				{
					round.SetPhase(Phase.Ended);
					round.LogInfo($"Not enough inputs ({round.InputCount}) in {nameof(Phase.InputRegistration)} phase.");
				}
				else
				{
					round.SetPhase(Phase.ConnectionConfirmation);
				}
			}
		}

		private void StepConnectionConfirmationPhase()
		{
			foreach (var round in Rounds.Where(x => x.Phase == Phase.ConnectionConfirmation).ToArray())
			{
				if (round.Alices.All(x => x.ConfirmedConnection))
				{
					round.SetPhase(Phase.OutputRegistration);
				}
				else if (round.ConnectionConfirmationStart + round.ConnectionConfirmationTimeout < DateTimeOffset.UtcNow)
				{
					var alicesDidntConfirm = round.Alices.Where(x => !x.ConfirmedConnection).ToArray();
					foreach (var alice in alicesDidntConfirm)
					{
						Prison.Note(alice, round.Id);
					}
					var removedAliceCount = round.Alices.RemoveAll(x => alicesDidntConfirm.Contains(x));
					round.LogInfo($"{removedAliceCount} alices removed because they didn't confirm.");

					if (round.InputCount < Config.MinInputCountByRound)
					{
						round.SetPhase(Phase.Ended);
						round.LogInfo($"Not enough inputs ({round.InputCount}) in {nameof(Phase.ConnectionConfirmation)} phase.");
					}
					else
					{
						round.SetPhase(Phase.OutputRegistration);
					}
				}
			}
		}

		private void StepOutputRegistrationPhase()
		{
			foreach (var round in Rounds.Where(x => x.Phase == Phase.OutputRegistration).ToArray())
			{
				long aliceSum = round.Alices.Sum(x => x.CalculateRemainingAmountCredentials(round.FeeRate));
				long bobSum = round.Bobs.Sum(x => x.CredentialAmount);
				var diff = aliceSum - bobSum;
				var allReady = round.Alices.All(a => a.ReadyToSign);

				if (allReady || round.OutputRegistrationStart + round.OutputRegistrationTimeout < DateTimeOffset.UtcNow)
				{
					var coinjoin = round.Assert<ConstructionState>();

					round.LogInfo($"{coinjoin.Inputs.Count} inputs were added.");
					round.LogInfo($"{coinjoin.Outputs.Count} outputs were added.");

					// If timeout we must fill up the outputs to build a reasonable transaction.
					// This won't be signed by the alice who failed to provide output, so we know who to ban.
					var diffMoney = Money.Satoshis(diff) - coinjoin.Parameters.FeeRate.GetFee(Config.BlameScript.EstimateOutputVsize());
					if (!allReady && diffMoney > coinjoin.Parameters.AllowedOutputAmounts.Min)
					{
						coinjoin = coinjoin.AddOutput(new TxOut(diffMoney, Config.BlameScript));
						round.LogInfo("Filled up the outputs to build a reasonable transaction because some alice failed to provide its output.");
					}

					round.CoinjoinState = coinjoin.Finalize();

					round.SetPhase(Phase.TransactionSigning);
				}
			}
		}

		private async Task StepTransactionSigningPhaseAsync()
		{
			foreach (var round in Rounds.Where(x => x.Phase == Phase.TransactionSigning).ToArray())
			{
				var state = round.Assert<SigningState>();

				try
				{
					if (state.IsFullySigned)
					{
						var coinjoin = state.CreateTransaction();

						// Logging.
						round.LogInfo("Trying to broadcast coinjoin.");
						Coin[]? spentCoins = round.Alices.Select(x => x.Coin).ToArray();
						Money networkFee = coinjoin.GetFee(spentCoins);
						uint256 roundId = round.Id;
						FeeRate feeRate = coinjoin.GetFeeRate(spentCoins);
						round.LogInfo($"Network Fee: {networkFee.ToString(false, false)} BTC.");
						round.LogInfo($"Network Fee Rate: {feeRate.FeePerK.ToDecimal(MoneyUnit.Satoshi) / 1000} sat/vByte.");
						round.LogInfo($"Number of inputs: {coinjoin.Inputs.Count}.");
						round.LogInfo($"Number of outputs: {coinjoin.Outputs.Count}.");
						round.LogInfo($"Serialized Size: {coinjoin.GetSerializedSize() / 1024} KB.");
						round.LogInfo($"VSize: {coinjoin.GetVirtualSize() / 1024} KB.");
						foreach (var (value, count) in coinjoin.GetIndistinguishableOutputs(includeSingle: false))
						{
							round.LogInfo($"There are {count} occurrences of {value.ToString(true, false)} BTC output.");
						}

						// Broadcasting.
						await Rpc.SendRawTransactionAsync(coinjoin).ConfigureAwait(false);
						round.WasTransactionBroadcast = true;
						round.SetPhase(Phase.Ended);

						round.LogInfo($"Successfully broadcast the CoinJoin: {coinjoin.GetHash()}.");
					}
					else if (round.TransactionSigningStart + round.TransactionSigningTimeout < DateTimeOffset.UtcNow)
					{
						throw new TimeoutException($"Round {round.Id}: Signing phase timed out after {round.TransactionSigningTimeout.TotalSeconds} seconds.");
					}
				}
				catch (Exception ex)
				{
					round.LogWarning($"Signing phase failed, reason: '{ex}'.");
					await FailTransactionSigningPhaseAsync(round).ConfigureAwait(false);
				}
			}
		}

		private async Task FailTransactionSigningPhaseAsync(Round round)
		{
			var state = round.Assert<SigningState>();

			var unsignedPrevouts = state.UnsignedInputs.ToHashSet();

			var alicesWhoDidntSign = round.Alices
				.Select(alice => (Alice: alice, alice.Coin))
				.Where(x => unsignedPrevouts.Contains(x.Coin))
				.Select(x => x.Alice)
				.ToHashSet();

			foreach (var alice in alicesWhoDidntSign)
			{
				Prison.Note(alice, round.Id);
			}

			round.Alices.RemoveAll(x => alicesWhoDidntSign.Contains(x));
			round.SetPhase(Phase.Ended);

			if (round.InputCount >= Config.MinInputCountByRound)
			{
				await CreateBlameRoundAsync(round).ConfigureAwait(false);
			}
		}

		private async Task CreateBlameRoundAsync(Round round)
		{
			var feeRate = (await Rpc.EstimateSmartFeeAsync((int)Config.ConfirmationTarget, EstimateSmartFeeMode.Conservative, simulateIfRegTest: true).ConfigureAwait(false)).FeeRate;
			RoundParameters parameters = new(Config, Network, Random, feeRate, blameOf: round);
			Round blameRound = new(parameters);
			if (!RoundsById.TryAdd(blameRound.Id, blameRound))
			{
				throw new InvalidOperationException();
			}
			Rounds.Add(blameRound);
		}

		private async Task CreateRoundsAsync()
		{
			if (!Rounds.Any(x => !x.IsBlameRound && x.Phase == Phase.InputRegistration))
			{
				var feeRate = (await Rpc.EstimateSmartFeeAsync((int)Config.ConfirmationTarget, EstimateSmartFeeMode.Conservative, simulateIfRegTest: true).ConfigureAwait(false)).FeeRate;

				RoundParameters roundParams = new(Config, Network, Random, feeRate);
				Round r = new(roundParams);
				if (!RoundsById.TryAdd(r.Id, r))
				{
					throw new InvalidOperationException();
				}
				Rounds.Add(r);
			}
		}

		private void TimeoutRounds()
		{
		    foreach (var expiredRound in Rounds.Where(
				x =>
				x.Phase == Phase.Ended
				&& x.End + Config.RoundExpiryTimeout < DateTimeOffset.UtcNow).ToArray())
			{
				Rounds.Remove(expiredRound);
			}
		}

		private void TimeoutAlices()
		{
			foreach (var round in Rounds.Where(x => !x.IsInputRegistrationEnded(Config.MaxInputCountByRound, Config.GetInputRegistrationTimeout(x))).ToArray())
			{
				// TODO remove from Alices container
				var removedAliceCount = round.Alices.RemoveAll(x => x.Deadline < DateTimeOffset.UtcNow);
				if (removedAliceCount > 0)
				{
					round.LogInfo($"{removedAliceCount} alices timed out and removed.");
				}
			}
		}

		public async Task<RoundState[]> GetStatusAsync(CancellationToken cancellationToken)
		{
			return RoundsById.Select(x => RoundState.FromRound(x.Value)).ToArray();
		}

		public async Task<InputRegistrationResponse> RegisterInputAsync(InputRegistrationRequest request)
		{
			if (RoundsById[request.RoundId] is not Round round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			var coin = await OutpointToCoinAsync(request).ConfigureAwait(false);

			var alice = new Alice(coin, request.OwnershipProof, round);

			// Begin with Alice locked, to serialize requests concerning a
			// single coin.
			using (await alice.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				// TODO cleanup on subsequent error? it can't be removed by aliceid
				if (!AlicesByOutpoint.TryAdd(coin.Outpoint, alice))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceAlreadyRegistered);
				}

				using (await AsyncLock.LockAsync().ConfigureAwait(false))
				{
					if (round.IsInputRegistrationEnded(Config.MaxInputCountByRound, Config.GetInputRegistrationTimeout(round)))
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
					}

					if (round.IsBlameRound && !round.BlameWhitelist.Contains(alice.Coin.Outpoint))
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputNotWhitelisted);
					}

					// Compute but don't commit updated CoinJoin to round state, it will
					// be re-calculated on input confirmation. This is computed it here
					// for validation purposes.
					round.Assert<ConstructionState>().AddInput(alice.Coin);
				}

				var coinJoinInputCommitmentData = new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", round.Id);
				if (!OwnershipProof.VerifyCoinJoinInputProof(alice.OwnershipProof, alice.Coin.TxOut.ScriptPubKey, coinJoinInputCommitmentData))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongOwnershipProof);
				}

				if (alice.TotalInputAmount < round.MinRegistrableAmount)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.NotEnoughFunds);
				}
				if (alice.TotalInputAmount > round.MaxRegistrableAmount)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchFunds);
				}

				if (alice.TotalInputVsize > round.MaxVsizeAllocationPerAlice)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.TooMuchVsize);
				}

				if (round.RemainingInputVsizeAllocation < round.MaxVsizeAllocationPerAlice)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.VsizeQuotaExceeded);
				}

				var zeroAmountCredentialRequests = request.ZeroAmountCredentialRequests;
				var zeroVsizeCredentialRequests = request.ZeroVsizeCredentialRequests;

				var commitAmountCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(zeroAmountCredentialRequests);
				var commitVsizeCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(zeroVsizeCredentialRequests);

				using (await AsyncLock.LockAsync().ConfigureAwait(false))
				{
					// Check that everything is the same
					if (round.IsInputRegistrationEnded(Config.MaxInputCountByRound, Config.GetInputRegistrationTimeout(round)))
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase);
					}

					alice.SetDeadlineRelativeTo(round.ConnectionConfirmationTimeout);
					round.Alices.Add(alice);
				}

				// Now that alice is in the round, make it available by id.
				if (!AlicesById.TryAdd(alice.Id, alice))
				{
					throw new InvalidOperationException();
				}

				return new(alice.Id,
						   commitAmountCredentialResponse.Commit(),
						   commitVsizeCredentialResponse.Commit());
			}
		}

		private async Task<Coin> OutpointToCoinAsync(InputRegistrationRequest request)
		{
			OutPoint input = request.Input;

			if (Prison.TryGet(input, out var inmate) && (!Config.AllowNotedInputRegistration || inmate.Punishment != Punishment.Noted))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputBanned);
			}

			var txOutResponse = await Rpc.GetTxOutAsync(input.Hash, (int)input.N, includeMempool: true).ConfigureAwait(false);
			if (txOutResponse is null)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputSpent);
			}
			if (txOutResponse.Confirmations == 0)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputUnconfirmed);
			}
			if (txOutResponse.IsCoinBase && txOutResponse.Confirmations <= 100)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.InputImmature);
			}

			return new Coin(input, txOutResponse.TxOut);
		}

		public async Task ReadyToSignAsync(ReadyToSignRequestRequest request)
		{
			if (RoundsById[request.RoundId] is not Round round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			if (AlicesById[request.AliceId] is not Alice alice || alice.Round != round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
			}

			using (await alice.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				var coinJoinInputCommitmentData = new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", request.RoundId);
				if (!OwnershipProof.VerifyCoinJoinInputProof(request.OwnershipProof, alice.Coin.TxOut.ScriptPubKey, coinJoinInputCommitmentData))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongOwnershipProof);
				}

				using (await AsyncLock.LockAsync().ConfigureAwait(false))
				{
					alice.ReadyToSign = true;
				}
			}
		}

		public async Task RemoveInputAsync(InputsRemovalRequest request)
		{
			if (RoundsById[request.RoundId] is not Round round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			if (AlicesById[request.AliceId] is not Alice alice || alice.Round != round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
			}

			using (await alice.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				using (await AsyncLock.LockAsync().ConfigureAwait(false))
				{
					if (round.Phase != Phase.InputRegistration)
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
					}

					// At this point ownership proofs have not yet been revealed
					// to other participants, so AliceId can only be known to
					// its owner.
					round.Alices.Remove(alice);
				}

				AlicesById.Remove(alice.Id, out _);
				AlicesByOutpoint.Remove(alice.Coin.Outpoint, out _);
			}
		}

		public async Task<ConnectionConfirmationResponse> ConfirmConnectionAsync(ConnectionConfirmationRequest request)
		{
			if (RoundsById[request.RoundId] is not Round round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			if (AlicesById[request.AliceId] is not Alice alice || alice.Round != round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.AliceNotFound, $"Round ({request.RoundId}): Alice ({request.AliceId}) not found.");
			}

			using (await alice.AsyncLock.LockAsync().ConfigureAwait(false))
			{
				var realAmountCredentialRequests = request.RealAmountCredentialRequests;
				var realVsizeCredentialRequests = request.RealVsizeCredentialRequests;

				if (realVsizeCredentialRequests.Delta != alice.CalculateRemainingVsizeCredentials(round.MaxVsizeAllocationPerAlice))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
				}
				if (realAmountCredentialRequests.Delta != alice.CalculateRemainingAmountCredentials(round.FeeRate))
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedAmountCredentials, $"Round ({request.RoundId}): Incorrect requested amount credentials.");
				}

				var commitAmountZeroCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(request.ZeroAmountCredentialRequests);
				var commitVsizeZeroCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(request.ZeroVsizeCredentialRequests);

				using (await AsyncLock.LockAsync().ConfigureAwait(false))
				{
					if (round.Phase == Phase.InputRegistration)
					{
						alice.SetDeadlineRelativeTo(round.ConnectionConfirmationTimeout);

						return new(
							commitAmountZeroCredentialResponse.Commit(),
							commitVsizeZeroCredentialResponse.Commit());
					}
					else if (round.Phase == Phase.ConnectionConfirmation)
					{
						// Ensure the input can be added to the CoinJoin
						round.Assert<ConstructionState>().AddInput(alice.Coin);
					}
					else
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
					}
				}

				// Connection confirmation phase, verify the range proofs
				var commitAmountRealCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(realAmountCredentialRequests);
				var commitVsizeRealCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(realVsizeCredentialRequests);

				// Re-acquire lock to commit confirmation
				using (await AsyncLock.LockAsync().ConfigureAwait(false))
				{
					if (round.Phase != Phase.ConnectionConfirmation)
					{
						throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
					}

					var state = round.Assert<ConstructionState>();

					// Ensure the input can still be added to the CoinJoin
					state = state.AddInput(alice.Coin);

					// update state
					alice.ConfirmedConnection = true;
					round.CoinjoinState = state;

					return new(
						commitAmountZeroCredentialResponse.Commit(),
						commitVsizeZeroCredentialResponse.Commit(),
						commitAmountRealCredentialResponse.Commit(),
						commitVsizeRealCredentialResponse.Commit());
				}
			}
		}

		public async Task<OutputRegistrationResponse> RegisterOutputAsync(OutputRegistrationRequest request)
		{
			if (RoundsById[request.RoundId] is not Round round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			var credentialAmount = -request.AmountCredentialRequests.Delta;

			Bob bob = new(request.Script, credentialAmount);

			var outputValue = bob.CalculateOutputAmount(round.FeeRate);

			var vsizeCredentialRequests = request.VsizeCredentialRequests;
			if (-vsizeCredentialRequests.Delta != bob.OutputVsize)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.IncorrectRequestedVsizeCredentials, $"Round ({request.RoundId}): Incorrect requested vsize credentials.");
			}

			if (round.Phase != Phase.OutputRegistration)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
			}

			// Calculate state with the additional output to ensure it's valid.
			_ = round.AddOutput(new TxOut(outputValue, bob.Script));

			// Verify the credential requests and prepare their responses.
			var commitAmountCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(request.AmountCredentialRequests);
			var commitVsizeCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(vsizeCredentialRequests);

			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				// Check to ensure phase is still valid
				if (round.Phase != Phase.OutputRegistration)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
				}

				// Recalculate state, since it may have been updated. Success of
				// inclusion is guaranteed if it succeeded at the previous
				// state, because the total output vsize is limited by the vsize
				// credentials, and all other conditions are state invariant.
				var newState = round.AddOutput(new TxOut(outputValue, bob.Script));

				// Update round state.
				round.Bobs.Add(bob);
				round.CoinjoinState = newState;
			}

			// Issue credentials and mark presented credentials as used.
			return new(
				commitAmountCredentialResponse.Commit(),
				commitVsizeCredentialResponse.Commit());
		}

		public async Task SignTransactionAsync(TransactionSignaturesRequest request)
		{
			if (RoundsById[request.RoundId] is not Round round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			using (await AsyncLock.LockAsync().ConfigureAwait(false))
			{
				if (round.Phase != Phase.TransactionSigning)
				{
					throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({request.RoundId}): Wrong phase ({round.Phase}).");
				}

				var state = round.Assert<SigningState>();
				foreach (var inputWitnessPair in request.InputWitnessPairs)
				{
					state = state.AddWitness((int)inputWitnessPair.InputIndex, inputWitnessPair.Witness);
				}

				// at this point all of the witnesses have been verified and the state can be updated
				round.CoinjoinState = state;
			}
		}

		public async Task<ReissueCredentialResponse> ReissuanceAsync(ReissueCredentialRequest request)
		{
			if (RoundsById[request.RoundId] is not Round round)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.RoundNotFound, $"Round ({request.RoundId}) not found.");
			}

			if (round.Phase is not (Phase.ConnectionConfirmation or Phase.OutputRegistration))
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongPhase, $"Round ({round.Id}): Wrong phase ({round.Phase}).");
			}

			if (request.RealAmountCredentialRequests.Delta != 0)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.DeltaNotZero, $"Round ({round.Id}): Amount credentials delta must be zero.");
			}

			if (request.RealAmountCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({round.Id}): Incorrect requested number of amount credentials.");
			}

			if (request.RealVsizeCredentialRequests.Requested.Count() != ProtocolConstants.CredentialNumber)
			{
				throw new WabiSabiProtocolException(WabiSabiProtocolErrorCode.WrongNumberOfCreds, $"Round ({round.Id}): Incorrect requested number of weight credentials.");
			}

			var commitRealAmountCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(request.RealAmountCredentialRequests);
			var commitRealVsizeCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(request.RealVsizeCredentialRequests);
			var commitZeroAmountCredentialResponse = round.AmountCredentialIssuer.PrepareResponse(request.ZeroAmountCredentialRequests);
			var commitZeroVsizeCredentialResponse = round.VsizeCredentialIssuer.PrepareResponse(request.ZeroVsizeCredentialsRequests);

			return new(
				commitRealAmountCredentialResponse.Commit(),
				commitRealVsizeCredentialResponse.Commit(),
				commitZeroAmountCredentialResponse.Commit(),
				commitZeroVsizeCredentialResponse.Commit()
			);
		}

		public override void Dispose()
		{
			Random.Dispose();
			base.Dispose();
		}
	}
}
