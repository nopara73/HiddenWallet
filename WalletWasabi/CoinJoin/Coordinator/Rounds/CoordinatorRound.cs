using NBitcoin;
using NBitcoin.RPC;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.CoinJoin.Common.Models;
using WalletWasabi.CoinJoin.Coordinator.Banning;
using WalletWasabi.CoinJoin.Coordinator.MixingLevels;
using WalletWasabi.CoinJoin.Coordinator.Participants;
using WalletWasabi.Crypto;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using static WalletWasabi.Crypto.SchnorrBlinding;

namespace WalletWasabi.CoinJoin.Coordinator.Rounds
{
	public class CoordinatorRound
	{
		public static long RoundCount;
		private RoundPhase _phase;
		private CoordinatorRoundStatus _status;

		public CoordinatorRound(IRPCClient rpc, UtxoReferee utxoReferee, CoordinatorRoundConfig config, int adjustedConfirmationTarget, int configuredConfirmationTarget, double configuredConfirmationTargetReductionRate, TimeSpan inputRegistrationTimeOut)
		{
			try
			{
				RoundId = Interlocked.Increment(ref RoundCount);

				RpcClient = Guard.NotNull(nameof(rpc), rpc);
				UtxoReferee = Guard.NotNull(nameof(utxoReferee), utxoReferee);
				RoundConfig = Guard.NotNull(nameof(config), config);

				AdjustedConfirmationTarget = adjustedConfirmationTarget;
				ConfiguredConfirmationTarget = configuredConfirmationTarget;
				ConfiguredConfirmationTargetReductionRate = configuredConfirmationTargetReductionRate;
				CoordinatorFeePercent = config.CoordinatorFeePercent;
				AnonymitySet = config.AnonymitySet;
				InputRegistrationTimeout = inputRegistrationTimeOut;
				SetInputRegistrationTimesout();
				ConnectionConfirmationTimeout = TimeSpan.FromSeconds(config.ConnectionConfirmationTimeout);
				OutputRegistrationTimeout = TimeSpan.FromSeconds(config.OutputRegistrationTimeout);
				SigningTimeout = TimeSpan.FromSeconds(config.SigningTimeout);

				PhaseLock = new object();
				Phase = RoundPhase.InputRegistration;
				StatusLock = new object();
				Status = CoordinatorRoundStatus.NotStarted;

				RegisteredUnblindedSignatures = new List<UnblindedSignature>();
				RegisteredUnblindedSignaturesLock = new object();

				NonceProvider = new RoundNonceProvider(config.MaximumMixingLevelCount);

				MixingLevels = new MixingLevelCollection(config.Denomination, new Signer(new Key()));
				for (int i = 0; i < config.MaximumMixingLevelCount - 1; i++)
				{
					MixingLevels.AddNewLevel();
				}

				CoinJoin = null;

				Alices = new List<Alice>();
				Bobs = new List<Bob>();

				Logger.LogInfo($"Round ({RoundId}): New round is created.\n\t" +
					$"BaseDenomination: {MixingLevels.GetBaseDenomination().ToString(false, true)} BTC.\n\t" +
					$"{nameof(AdjustedConfirmationTarget)}: {AdjustedConfirmationTarget}.\n\t" +
					$"{nameof(CoordinatorFeePercent)}: {CoordinatorFeePercent}%.\n\t" +
					$"{nameof(AnonymitySet)}: {AnonymitySet}.");
			}
			catch (Exception ex)
			{
				Logger.LogError($"Round ({RoundId}): Could not create.");
				Logger.LogError(ex);
				throw;
			}
		}

		public event EventHandler<RoundPhase>? PhaseChanged;

		public event EventHandler<CoordinatorRoundStatus>? StatusChanged;

		public event EventHandler<Transaction>? CoinJoinBroadcasted;

		public long RoundId { get; }

		public IRPCClient RpcClient { get; }
		public Network Network => RpcClient.Network;

		/// <summary>
		/// The confirmation target that will be used and possibly modified before final build.
		/// </summary>
		public int AdjustedConfirmationTarget { get; private set; }

		/// <summary>
		/// The confirmation target that is present in the config file.
		/// </summary>
		public int ConfiguredConfirmationTarget { get; }

		/// <summary>
		/// The rate of confirmation target reduction rate that is present in the config file.
		/// </summary>
		public double ConfiguredConfirmationTargetReductionRate { get; }

		public decimal CoordinatorFeePercent { get; }
		public int AnonymitySet { get; private set; }

		public Money FeePerInputs { get; private set; }
		public Money FeePerOutputs { get; private set; }

		public string UnsignedCoinJoinHex { get; private set; }

		public MixingLevelCollection MixingLevels { get; }

		public Transaction CoinJoin { get; private set; }

		private List<Alice> Alices { get; }
		private List<Bob> Bobs { get; } // Do not make it a hashset or do not make Bob IEquitable!!!

		private List<UnblindedSignature> RegisteredUnblindedSignatures { get; }
		private object RegisteredUnblindedSignaturesLock { get; }

		private static AsyncLock RoundSynchronizerLock { get; } = new AsyncLock();
		public static AsyncLock ConnectionConfirmationLock { get; } = new AsyncLock();

		private object PhaseLock { get; }

		public RoundPhase Phase
		{
			get
			{
				lock (PhaseLock)
				{
					return _phase;
				}
			}

			private set
			{
				var invoke = false;
				lock (PhaseLock)
				{
					if (_phase != value)
					{
						_phase = value;
						invoke = true;
					}
				}
				if (invoke)
				{
					PhaseChanged?.Invoke(this, value);
				}
			}
		}

		private object StatusLock { get; }

		public CoordinatorRoundStatus Status
		{
			get
			{
				lock (StatusLock)
				{
					return _status;
				}
			}

			private set
			{
				var invoke = false;
				lock (StatusLock)
				{
					if (_status != value)
					{
						_status = value;
						invoke = true;
					}
				}
				if (invoke)
				{
					StatusChanged?.Invoke(this, value);
				}
			}
		}

		public TimeSpan AliceRegistrationTimeout => ConnectionConfirmationTimeout;

		public TimeSpan InputRegistrationTimeout { get; }
		public DateTimeOffset InputRegistrationTimesout { get; private set; }

		public TimeSpan RemainingInputRegistrationTime
		{
			get
			{
				var remaining = InputRegistrationTimesout - DateTimeOffset.UtcNow;
				if (Phase == RoundPhase.InputRegistration && remaining > TimeSpan.Zero)
				{
					return remaining;
				}
				else
				{
					return TimeSpan.Zero;
				}
			}
		}

		public TimeSpan ConnectionConfirmationTimeout { get; }

		public TimeSpan OutputRegistrationTimeout { get; }

		public TimeSpan SigningTimeout { get; }

		public UtxoReferee UtxoReferee { get; }
		public CoordinatorRoundConfig RoundConfig { get; }

		public RoundNonceProvider NonceProvider { get; }

		public static ConcurrentDictionary<(long roundId, RoundPhase phase), DateTimeOffset> PhaseTimeoutLog { get; } = new ConcurrentDictionary<(long roundId, RoundPhase phase), DateTimeOffset>();

		private void SetInputRegistrationTimesout()
		{
			InputRegistrationTimesout = DateTimeOffset.UtcNow + InputRegistrationTimeout;
		}

		public async Task ExecuteNextPhaseAsync(RoundPhase expectedPhase)
		{
			using (await RoundSynchronizerLock.LockAsync().ConfigureAwait(false))
			{
				try
				{
					Logger.LogInfo($"Round ({RoundId}): Phase change requested: {expectedPhase}.");

					if (Status == CoordinatorRoundStatus.NotStarted) // So start the input registration phase.
					{
						if (expectedPhase != RoundPhase.InputRegistration)
						{
							return;
						}

						await MoveToInputRegistrationAsync().ConfigureAwait(false);
					}
					else if (Status != CoordinatorRoundStatus.Running) // Aborted or succeeded, swallow.
					{
						return;
					}
					else if (Phase == RoundPhase.InputRegistration)
					{
						if (expectedPhase != RoundPhase.ConnectionConfirmation)
						{
							return;
						}

						await MoveToConnectionConfirmationAsync().ConfigureAwait(false);
					}
					else if (Phase == RoundPhase.ConnectionConfirmation)
					{
						if (expectedPhase != RoundPhase.OutputRegistration)
						{
							return;
						}

						MoveToOutputRegistration();
					}
					else if (Phase == RoundPhase.OutputRegistration)
					{
						if (expectedPhase != RoundPhase.Signing)
						{
							return;
						}

						await MoveToSigningAsync().ConfigureAwait(false);
					}
					else // Phase == RoundPhase.Signing
					{
						return;
					}

					Logger.LogInfo($"Round ({RoundId}): Phase initialized: {expectedPhase}.");
				}
				catch (Exception ex)
				{
					Logger.LogError(ex);
					Status = CoordinatorRoundStatus.Aborted;
					throw;
				}
			}

			KickTimeout(expectedPhase);
		}

		private void KickTimeout(RoundPhase phase)
		{
			_ = Task.Run(async () =>
			{
				try
				{
					TimeSpan timeout = GetTimeout(phase);

					// Delay asynchronously to the requested timeout.
					await Task.Delay(timeout).ConfigureAwait(false);

					var executeRunAbortion = false;
					using (await RoundSynchronizerLock.LockAsync().ConfigureAwait(false))
					{
						executeRunAbortion = Status == CoordinatorRoundStatus.Running && Phase == phase;
					}
					if (executeRunAbortion)
					{
						PhaseTimeoutLog.TryAdd((RoundId, Phase), DateTimeOffset.UtcNow);
						string timedOutLogString = $"Round ({RoundId}): {phase} timed out after {timeout.TotalSeconds} seconds.";

						if (phase == RoundPhase.ConnectionConfirmation)
						{
							Logger.LogInfo(timedOutLogString);
						}
						else if (phase == RoundPhase.OutputRegistration)
						{
							Logger.LogInfo($"Round ({RoundId}): {timedOutLogString} Progressing to signing phase to blame...");
						}
						else
						{
							Logger.LogInfo($"Round ({RoundId}): {timedOutLogString} Aborting...");
						}

						// This will happen outside the lock.
						_ = Task.Run(async () =>
						{
							try
							{
								switch (phase)
								{
									case RoundPhase.InputRegistration:
										{
											// Only abort if less than two one Alice is registered.
											// Do not ban anyone, it's ok if they lost connection.
											await RemoveAlicesIfAnInputRefusedByMempoolAsync().ConfigureAwait(false);
											int aliceCountAfterInputRegistrationTimeout = CountAlices();
											if (aliceCountAfterInputRegistrationTimeout < 2)
											{
												Abort($"Only {aliceCountAfterInputRegistrationTimeout} Alices registered.");
											}
											else
											{
												UpdateAnonymitySet(aliceCountAfterInputRegistrationTimeout);
												// Progress to the next phase, which will be ConnectionConfirmation
												await ExecuteNextPhaseAsync(RoundPhase.ConnectionConfirmation).ConfigureAwait(false);
											}
										}
										break;

									case RoundPhase.ConnectionConfirmation:
										{
											using (await ConnectionConfirmationLock.LockAsync().ConfigureAwait(false))
											{
												IEnumerable<Alice> alicesToBan = GetAlicesBy(AliceState.InputsRegistered);

												await ProgressToOutputRegistrationOrFailAsync(alicesToBan.ToArray()).ConfigureAwait(false);
											}
										}
										break;

									case RoundPhase.OutputRegistration:
										{
											// Output registration never aborts.
											// We do not know which Alice to ban.
											// Therefore proceed to signing, and whichever Alice does not sign, ban her.
											await ExecuteNextPhaseAsync(RoundPhase.Signing).ConfigureAwait(false);
										}
										break;

									case RoundPhase.Signing:
										{
											Alice[] alicesToBan = GetAlicesByNot(AliceState.SignedCoinJoin, syncLock: true).ToArray();

											if (alicesToBan.Any())
											{
												await UtxoReferee.BanUtxosAsync(1, DateTimeOffset.UtcNow, forceNoted: false, RoundId, alicesToBan.SelectMany(x => x.Inputs.Select(y => y.Outpoint)).ToArray()).ConfigureAwait(false);
											}

											Abort($"{alicesToBan.Length} Alices did not sign.");
										}
										break;

									default:
										throw new InvalidOperationException("This should never happen.");
								}
							}
							catch (Exception ex)
							{
								Logger.LogWarning($"Round ({RoundId}): {phase} timeout failed.");
								Logger.LogWarning(ex);
							}
						}).ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					Logger.LogWarning($"Round ({RoundId}): {phase} timeout failed.");
					Logger.LogWarning(ex);
				}
			}).ConfigureAwait(false);
		}

		private TimeSpan GetTimeout(RoundPhase phase)
		{
			TimeSpan timeout;
			switch (phase)
			{
				case RoundPhase.InputRegistration:
					{
						SetInputRegistrationTimesout(); // Update it, it's going to be slightly more accurate.
						timeout = InputRegistrationTimeout;
					}
					break;

				case RoundPhase.ConnectionConfirmation:
					timeout = ConnectionConfirmationTimeout;
					break;

				case RoundPhase.OutputRegistration:
					timeout = OutputRegistrationTimeout;
					break;

				case RoundPhase.Signing:
					timeout = SigningTimeout;
					break;

				default:
					throw new InvalidOperationException("This should never happen.");
			}

			return timeout;
		}

		private async Task MoveToSigningAsync()
		{
			// Build CoinJoin:
			// 1. Set new denomination: minor optimization.
			Money newDenomination = CalculateNewDenomination();
			var transaction = Network.Consensus.ConsensusFactory.CreateTransaction();

			// 2. If there are less Bobs than Alices, then add our own address. The malicious Alice, who will refuse to sign.
			var derivationIndex = RoundConfig.CoordinatorExtPubKeyCurrentDepth + 1;
			for (int i = 0; i < MixingLevels.Count(); i++)
			{
				var aliceCountInLevel = Alices.Count(x => i < x.BlindedOutputScripts.Length);
				var missingBobCount = aliceCountInLevel - Bobs.Count(x => x.Level == MixingLevels.GetLevel(i));
				for (int j = 0; j < missingBobCount; j++)
				{
					var denomination = MixingLevels.GetLevel(i).Denomination;
					transaction.Outputs.AddWithOptimize(denomination, RoundConfig.DeriveCoordinatorScript(derivationIndex));
					derivationIndex++;
				}
			}

			// 3. Add Bob outputs.
			foreach (Bob bob in Bobs.Where(x => x.Level == MixingLevels.GetBaseLevel()))
			{
				transaction.Outputs.AddWithOptimize(newDenomination, bob.ActiveOutputAddress.ScriptPubKey);
			}

			// 3.1 newDenomination may differ from the Denomination at registration, so we may not be able to tinker with additional outputs.
			bool tinkerWithAdditionalMixingLevels = CanUseAdditionalOutputs(newDenomination);

			if (tinkerWithAdditionalMixingLevels)
			{
				foreach (MixingLevel level in MixingLevels.GetLevelsExceptBase())
				{
					IEnumerable<Bob> bobsOnThisLevel = Bobs.Where(x => x.Level == level);
					if (bobsOnThisLevel.Count() <= 1)
					{
						break;
					}

					foreach (Bob bob in bobsOnThisLevel)
					{
						transaction.Outputs.AddWithOptimize(level.Denomination, bob.ActiveOutputAddress.ScriptPubKey);
					}
				}
			}

			// 4. Start building Coordinator fee.
			var baseDenominationOutputCount = transaction.Outputs.Count(x => x.Value == newDenomination);
			Money coordinatorBaseFeePerAlice = newDenomination.Percentage(CoordinatorFeePercent * baseDenominationOutputCount);
			Money coordinatorFee = baseDenominationOutputCount * coordinatorBaseFeePerAlice;

			if (tinkerWithAdditionalMixingLevels)
			{
				foreach (MixingLevel level in MixingLevels.GetLevelsExceptBase())
				{
					var denominationOutputCount = transaction.Outputs.Count(x => x.Value == level.Denomination);
					if (denominationOutputCount <= 1)
					{
						break;
					}

					Money coordinatorLevelFeePerAlice = level.Denomination.Percentage(CoordinatorFeePercent * denominationOutputCount);
					coordinatorFee += coordinatorLevelFeePerAlice * denominationOutputCount;
				}
			}

			// 5. Add the inputs and the changes of Alices.
			var spentCoins = new List<Coin>();
			foreach (Alice alice in Alices)
			{
				foreach (var input in alice.Inputs)
				{
					transaction.Inputs.Add(new TxIn(input.Outpoint));
					spentCoins.Add(input);
				}

				Money changeAmount = alice.InputSum;
				changeAmount -= alice.NetworkFeeToPayAfterBaseDenomination;
				changeAmount -= newDenomination;
				changeAmount -= coordinatorBaseFeePerAlice;

				if (tinkerWithAdditionalMixingLevels)
				{
					for (int i = 1; i < alice.BlindedOutputScripts.Length; i++)
					{
						MixingLevel level = MixingLevels.GetLevel(i);
						var denominationOutputCount = transaction.Outputs.Count(x => x.Value == level.Denomination);
						if (denominationOutputCount <= 1)
						{
							break;
						}

						changeAmount -= FeePerOutputs;
						changeAmount -= level.Denomination;
						changeAmount -= level.Denomination.Percentage(CoordinatorFeePercent * denominationOutputCount);
					}
				}

				if (changeAmount > Money.Zero)
				{
					Money minimumOutputAmount = Money.Coins(0.0001m); // If the change would be less than about $1 then add it to the coordinator.
					Money somePercentOfDenomination = newDenomination.Percentage(0.3m); // If the change is less than about 0.3% of the newDenomination then add it to the coordinator fee.
					Money minimumChangeAmount = Math.Max(minimumOutputAmount, somePercentOfDenomination);
					if (changeAmount < minimumChangeAmount)
					{
						coordinatorFee += changeAmount;
					}
					else
					{
						transaction.Outputs.AddWithOptimize(changeAmount, alice.ChangeOutputAddress.ScriptPubKey);
					}
				}
				else
				{
					// If the coordinator fee would make change amount to be negative or zero,
					// i.e. Alice has no money enough to pay the coordinator fee then allow her to pay what she can.
					coordinatorFee += changeAmount;
				}
			}

			var coordinatorScript = RoundConfig.GetNextCleanCoordinatorScript();
			// 6. Add Coordinator fee only if > about $3, else just let it to be miner fee.
			if (coordinatorFee > Money.Coins(0.0003m))
			{
				transaction.Outputs.AddWithOptimize(coordinatorFee, coordinatorScript);
			}

			// 7. Try optimize fees.
			await TryOptimizeFeesAsync(transaction, spentCoins).ConfigureAwait(false);

			// 8. Shuffle.
			transaction.Inputs.Shuffle();
			transaction.Outputs.Shuffle();

			// 9. Sort inputs and outputs by amount so the coinjoin looks better in a block explorer.
			transaction.Inputs.SortByAmount(spentCoins);
			transaction.Outputs.SortByAmount();
			// Note: We shuffle then sort because inputs and outputs could have equal values

			if (transaction.Outputs.Any(x => x.ScriptPubKey == coordinatorScript))
			{
				RoundConfig.MakeNextCoordinatorScriptDirty();
			}

			CoinJoin = transaction;
			UnsignedCoinJoinHex = transaction.ToHex();

			Phase = RoundPhase.Signing;
		}

		/// <summary>
		/// This may result in a phase change, too.
		/// </summary>
		/// <returns>The signatures.</returns>
		public async Task<uint256[]> ConfirmAliceConnectionAsync(Alice alice)
		{
			uint256[] signatures;
			bool progessToOutputRegistration = false;
			using (await RoundSynchronizerLock.LockAsync().ConfigureAwait(false))
			{
				alice.State = AliceState.ConnectionConfirmed;

				int takeBlindCount = EstimateBestMixingLevel(alice);

				alice.BlindedOutputScripts = alice.BlindedOutputScripts[..takeBlindCount];
				alice.BlindedOutputSignatures = alice.BlindedOutputSignatures[..takeBlindCount];
				signatures = alice.BlindedOutputSignatures; // Do not give back more mixing levels than we'll use.

				// Progress round if needed.
				progessToOutputRegistration = Alices.All(x => x.State == AliceState.ConnectionConfirmed);
			}
			if (progessToOutputRegistration)
			{
				await ProgressToOutputRegistrationOrFailAsync().ConfigureAwait(false);
			}
			return signatures;
		}

		private void MoveToOutputRegistration()
		{
			Phase = RoundPhase.OutputRegistration;
		}

		private async Task MoveToConnectionConfirmationAsync()
		{
			await RemoveAlicesIfAnInputRefusedByMempoolNoLockAsync().ConfigureAwait(false);

			Phase = RoundPhase.ConnectionConfirmation;
		}

		private async Task MoveToInputRegistrationAsync()
		{
			// Calculate fees.
			(Money feePerInputs, Money feePerOutputs) fees = await CalculateFeesAsync(RpcClient, AdjustedConfirmationTarget).ConfigureAwait(false);
			FeePerInputs = fees.feePerInputs;
			FeePerOutputs = fees.feePerOutputs;

			Status = CoordinatorRoundStatus.Running;
		}

		private Money CalculateNewDenomination()
		{
			var newDenomination = Alices.Min(x => x.InputSum - x.NetworkFeeToPayAfterBaseDenomination);
			var collision = MixingLevels.GetLevelsExceptBase().FirstOrDefault(x => x.Denomination == newDenomination);
			if (collision is { })
			{
				newDenomination -= Money.Satoshis(1);
				Logger.LogDebug($"This should never happen. The new base denomination is exactly the same as the one of the mixing level. Adjusted the new denomination one satoshi less.");
			}

			return newDenomination;
		}

		private async Task ProgressToOutputRegistrationOrFailAsync(params Alice[] alicesNotConfirmConnection)
		{
			var responses = await GetTxOutForAllInputsAsync().ConfigureAwait(false);
			var alicesSpent = responses.Where(x => x.resp is null).Select(x => x.alice).ToHashSet();
			IEnumerable<OutPoint> inputsToBan = alicesSpent.SelectMany(x => x.Inputs).Select(y => y.Outpoint).Concat(alicesNotConfirmConnection.SelectMany(x => x.Inputs).Select(y => y.Outpoint)).Distinct();

			var alicesNotConfirmConnectionIds = alicesNotConfirmConnection.Select(x => x.UniqueId).ToArray();

			if (inputsToBan.Any())
			{
				await UtxoReferee.BanUtxosAsync(1, DateTimeOffset.UtcNow, forceNoted: false, RoundId, inputsToBan.ToArray()).ConfigureAwait(false);

				var alicesConnectionConfirmedAndSpentCount = alicesSpent.Select(x => x.UniqueId).Except(alicesNotConfirmConnectionIds).Distinct().Count();
				if (alicesConnectionConfirmedAndSpentCount > 0)
				{
					Abort($"{alicesConnectionConfirmedAndSpentCount} Alices confirmed their connections but spent their inputs.");
					return;
				}
			}

			// It is ok to remove these Alices, because these did not get blind signatures.
			RemoveAlicesBy(alicesNotConfirmConnectionIds.Distinct().ToArray());

			int aliceCountAfterConnectionConfirmationTimeout = CountAlices();
			int didNotConfirmCount = AnonymitySet - aliceCountAfterConnectionConfirmationTimeout;
			if (aliceCountAfterConnectionConfirmationTimeout < 2)
			{
				Abort($"{didNotConfirmCount} Alices did not confirm their connection.");
			}
			else
			{
				if (didNotConfirmCount > 0)
				{
					// Adjust anonymity set.
					UpdateAnonymitySet(aliceCountAfterConnectionConfirmationTimeout);
				}
				// Progress to the next phase, which will be OutputRegistration
				await ExecuteNextPhaseAsync(RoundPhase.OutputRegistration).ConfigureAwait(false);
			}
		}

		private bool CanUseAdditionalOutputs(Money newDenomination)
		{
			// If all alices only registered one blinded output, then we cannot tinker with additional denomination.
			if (Alices.All(x => x.BlindedOutputScripts.Length == 1))
			{
				return false;
			}

			bool tinkerWithAdditionalMixingLevels = true;
			foreach (Alice alice in Alices)
			{
				// Check if inputs have enough coins.
				Money networkFeeToPay = (alice.Inputs.Count() * FeePerInputs) + (2 * FeePerOutputs);
				Money changeAmount = alice.InputSum - (newDenomination + networkFeeToPay);
				var acceptedBlindedOutputScriptsCount = 1;

				// Make sure we sign the proper number of additional blinded outputs.
				var moneySoFar = Money.Zero;
				for (int i = 1; i < alice.BlindedOutputScripts.Length; i++)
				{
					MixingLevel level = MixingLevels.GetLevel(i);
					int bobsOnThisLevel = Bobs.Count(x => x.Level == level);
					if (bobsOnThisLevel <= 1)
					{
						if (i == 1)
						{
							tinkerWithAdditionalMixingLevels = false;
						}
						break;
					}

					changeAmount -= level.Denomination + FeePerOutputs + level.Denomination.Percentage(CoordinatorFeePercent * bobsOnThisLevel);

					if (changeAmount < Money.Zero)
					{
						if (acceptedBlindedOutputScriptsCount < alice.BlindedOutputScripts.Length)
						{
							tinkerWithAdditionalMixingLevels = false;
						}
						break;
					}

					acceptedBlindedOutputScriptsCount++;
				}
			}

			return tinkerWithAdditionalMixingLevels;
		}

		public void AddRegisteredUnblindedSignature(UnblindedSignature unblindedSignature)
		{
			lock (RegisteredUnblindedSignaturesLock)
			{
				RegisteredUnblindedSignatures.Add(unblindedSignature);
			}
		}

		public bool ContainsRegisteredUnblindedSignature(UnblindedSignature unblindedSignature)
		{
			lock (RegisteredUnblindedSignaturesLock)
			{
				var unblindedSignatureBytes = unblindedSignature.ToBytes();
				return RegisteredUnblindedSignatures.Any(x => ByteHelpers.CompareFastUnsafe(x.ToBytes(), unblindedSignatureBytes));
			}
		}

		public int EstimateBestMixingLevel(Alice alice)
		{
			Money newDenomination = CalculateNewDenomination();

			// Check if inputs have enough coins.
			Money networkFeeToPay = (alice.Inputs.Count() * FeePerInputs) + (2 * FeePerOutputs);
			Money changeAmount = alice.InputSum - (newDenomination + networkFeeToPay);
			var acceptedBlindedOutputScriptsCount = 1;

			// Make sure we sign the proper number of additional blinded outputs.
			var moneySoFar = Money.Zero;
			for (int i = 1; i < alice.BlindedOutputScripts.Length; i++)
			{
				MixingLevel level = MixingLevels.GetLevel(i);
				var potentialAlicesOnThisLevel = Alices.Count(x => x.BlindedOutputSignatures.Length > i);
				if (potentialAlicesOnThisLevel <= 1)
				{
					break;
				}

				changeAmount -= level.Denomination + FeePerOutputs + level.Denomination.Percentage(CoordinatorFeePercent * potentialAlicesOnThisLevel);

				if (changeAmount < Money.Zero)
				{
					break;
				}

				acceptedBlindedOutputScriptsCount++;
			}

			return acceptedBlindedOutputScriptsCount;
		}

		internal async Task TryOptimizeFeesAsync(Transaction transaction, IEnumerable<Coin> spentCoins)
		{
			try
			{
				await TryOptimizeConfirmationTargetAsync(spentCoins.Select(x => x.Outpoint.Hash).ToHashSet()).ConfigureAwait(false);

				// 7.1. Estimate the current FeeRate. Note, there are no signatures yet!
				int estimatedSigSizeBytes = transaction.Inputs.Count * Constants.P2wpkhInputSizeInBytes;
				int estimatedFinalTxSize = transaction.GetSerializedSize() + estimatedSigSizeBytes;
				Money fee = transaction.GetFee(spentCoins.ToArray());

				// There is a currentFeeRate null check later.
				FeeRate currentFeeRate = null;
				if (fee is null)
				{
					Logger.LogError($"Round ({RoundId}): Cannot calculate CoinJoin transaction fee. Some spent coins are missing.");
				}
				else if (fee <= Money.Zero)
				{
					Logger.LogError($"Round ({RoundId}): CoinJoin transaction is not paying any fee. Fee: {fee.ToString(fplus: true)}, Total Inputs: {(Money)spentCoins.Sum(x => x.Amount)}, Total Outputs: {transaction.TotalOut}.");
				}
				else
				{
					currentFeeRate = new FeeRate(fee, estimatedFinalTxSize);
				}

				// 7.2. Get the most optimal FeeRate.
				FeeRate optimalFeeRate = (await RpcClient.EstimateSmartFeeAsync(AdjustedConfirmationTarget, EstimateSmartFeeMode.Conservative, simulateIfRegTest: true).ConfigureAwait(false)).FeeRate;

				if (optimalFeeRate is null || optimalFeeRate == FeeRate.Zero || currentFeeRate is null || currentFeeRate == FeeRate.Zero) // This would be really strange if it'd happen.
				{
					Logger.LogError($"Round ({RoundId}): This should never happen. {nameof(optimalFeeRate)}: {optimalFeeRate}, {nameof(currentFeeRate)}: {currentFeeRate}.");
				}
				else if (optimalFeeRate < currentFeeRate)
				{
					// 7.2 If the fee can be lowered, lower it.
					// 7.2.1. How much fee can we save?
					Money feeShouldBePaid = Money.Satoshis(estimatedFinalTxSize * (int)optimalFeeRate.SatoshiPerByte);
					Money toSave = fee - feeShouldBePaid;

					// 7.2.2. Get the outputs to divide the savings between.
					var indistinguishableOutputs = transaction.GetIndistinguishableOutputs(includeSingle: true).ToArray();
					int maxMixCount = indistinguishableOutputs.Max(x => x.count);
					Money bestMixAmount = indistinguishableOutputs.Where(x => x.count == maxMixCount).Max(x => x.value);
					int bestMixCount = indistinguishableOutputs.First(x => x.value == bestMixAmount).count;

					// 7.2.3. Get the savings per best mix outputs.
					long toSavePerBestMixOutputs = toSave.Satoshi / bestMixCount;

					// 7.2.4. Modify the best mix outputs in the transaction.
					if (toSavePerBestMixOutputs > 0)
					{
						foreach (TxOut output in transaction.Outputs.Where(x => x.Value == bestMixAmount))
						{
							output.Value += toSavePerBestMixOutputs;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning($"Round ({RoundId}): Failed to optimize fees. Fallback to normal fees.");
				Logger.LogWarning(ex);
			}
		}

		private async Task TryOptimizeConfirmationTargetAsync(ISet<uint256> transactionHashes)
		{
			try
			{
				// If the transaction does not spend unconfirmed coins then the confirmation target can be the one that's been set in the config.
				var originalConfirmationTarget = AdjustedConfirmationTarget;

				// Note that only dependents matter, spenders do not matter much or at all, they just allow this transaction to be confirmed faster.
				var dependents = await RpcClient.GetAllDependentsAsync(transactionHashes, includingProvided: true, likelyProvidedManyConfirmedOnes: true).ConfigureAwait(false);
				AdjustedConfirmationTarget = AdjustConfirmationTarget(dependents.Count, ConfiguredConfirmationTarget, ConfiguredConfirmationTargetReductionRate);

				if (originalConfirmationTarget != AdjustedConfirmationTarget)
				{
					Logger.LogInfo($"Round ({RoundId}): Confirmation target is optimized from {originalConfirmationTarget} blocks to {AdjustedConfirmationTarget} blocks.");
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning($"Round ({RoundId}): Failed to optimize confirmation target. Fallback using the original one: {AdjustedConfirmationTarget} blocks.");
				Logger.LogWarning(ex);
			}
		}

		public static async Task<(Money feePerInputs, Money feePerOutputs)> CalculateFeesAsync(IRPCClient rpc, int confirmationTarget)
		{
			Guard.NotNull(nameof(rpc), rpc);
			Guard.NotNull(nameof(confirmationTarget), confirmationTarget);

			Money feePerInputs = null;
			Money feePerOutputs = null;
			var inputSizeInBytes = (int)Math.Ceiling(((3 * Constants.P2wpkhInputSizeInBytes) + Constants.P2pkhInputSizeInBytes) / 4m);
			var outputSizeInBytes = Constants.OutputSizeInBytes;
			try
			{
				var feeRate = (await rpc.EstimateSmartFeeAsync(confirmationTarget, EstimateSmartFeeMode.Conservative, simulateIfRegTest: true).ConfigureAwait(false)).FeeRate;

				// Make sure min relay fee (1000 sat) is hit.
				feePerInputs = Math.Max(feeRate.GetFee(inputSizeInBytes), Money.Satoshis(500));
				feePerOutputs = Math.Max(feeRate.GetFee(outputSizeInBytes), Money.Satoshis(250));
			}
			catch (Exception ex)
			{
				// If fee has not been initialized once, fall back.
				if (feePerInputs is null || feePerOutputs is null)
				{
					var feePerBytes = Money.Satoshis(100); // 100 satoshi per byte

					// Make sure min relay fee (1000 sat) is hit.
					feePerInputs = Math.Max(feePerBytes * inputSizeInBytes, Money.Satoshis(500));
					feePerOutputs = Math.Max(feePerBytes * outputSizeInBytes, Money.Satoshis(250));
				}

				Logger.LogError(ex);
			}

			return (feePerInputs, feePerOutputs);
		}

		public void Succeed(bool syncLock = true)
		{
			if (syncLock)
			{
				using (RoundSynchronizerLock.Lock())
				{
					Status = CoordinatorRoundStatus.Succeded;
				}
			}
			else
			{
				Status = CoordinatorRoundStatus.Succeded;
			}
			Logger.LogInfo($"Round ({RoundId}): Succeeded.");
		}

		public void Abort(string reason, bool syncLock = true, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1)
		{
			if (syncLock)
			{
				using (RoundSynchronizerLock.Lock())
				{
					Status = CoordinatorRoundStatus.Aborted;
				}
			}
			else
			{
				Status = CoordinatorRoundStatus.Aborted;
			}

			if (string.IsNullOrWhiteSpace(reason))
			{
				Logger.LogInfo($"Round ({RoundId}): Aborted.", callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
			}
			else
			{
				Logger.LogInfo($"Round ({RoundId}): Aborted. Reason: {reason}.", callerFilePath: callerFilePath, callerLineNumber: callerLineNumber);
			}
		}

		public int CountAlices(bool syncLock = true)
		{
			if (syncLock)
			{
				using (RoundSynchronizerLock.Lock())
				{
					return Alices.Count;
				}
			}
			return Alices.Count;
		}

		public int CountBlindSignatures()
		{
			using (RoundSynchronizerLock.Lock())
			{
				return Alices.Sum(x => x.BlindedOutputSignatures.Length);
			}
		}

		public bool ContainsBlindedOutputScript(uint256 blindedOutputScript)
		{
			using (RoundSynchronizerLock.Lock())
			{
				foreach (Alice alice in Alices)
				{
					if (alice.BlindedOutputScripts.Contains(blindedOutputScript))
					{
						return true;
					}
				}
			}

			return false;
		}

		public bool ContainsAnyBlindedOutputScript(IEnumerable<uint256> blindedOutputScripts)
		{
			using (RoundSynchronizerLock.Lock())
			{
				foreach (var bis in blindedOutputScripts)
				{
					foreach (Alice alice in Alices)
					{
						if (alice.BlindedOutputScripts.Contains(bis))
						{
							return true;
						}
					}
				}
			}

			return false;
		}

		public bool ContainsInput(OutPoint input, out List<Alice> alices)
		{
			alices = new List<Alice>();

			using (RoundSynchronizerLock.Lock())
			{
				foreach (Alice alice in Alices)
				{
					if (alice.Inputs.Any(x => x.Outpoint == input))
					{
						alices.Add(alice);
					}
				}
			}

			return alices.Count > 0;
		}

		public int CountBobs(bool synchronized = true)
		{
			if (synchronized)
			{
				using (RoundSynchronizerLock.Lock())
				{
					return Bobs.Count;
				}
			}
			return Bobs.Count;
		}

		public IEnumerable<Alice> GetAlicesBy(AliceState state)
		{
			using (RoundSynchronizerLock.Lock())
			{
				return Alices.Where(x => x.State == state).ToList();
			}
		}

		public bool IsChangeOutputRegistered(BitcoinAddress changeOutputAddress)
		{
			using (RoundSynchronizerLock.Lock())
			{
				return Alices.Where(x => x.ChangeOutputAddress == changeOutputAddress).Any();
			}
		}

		public Alice TryGetAliceBy(Guid uniqueId)
		{
			using (RoundSynchronizerLock.Lock())
			{
				return Alices.SingleOrDefault(x => x.UniqueId == uniqueId);
			}
		}

		public IEnumerable<Alice> GetAlicesByNot(AliceState state, bool syncLock = true)
		{
			if (syncLock)
			{
				using (RoundSynchronizerLock.Lock())
				{
					return Alices.Where(x => x.State != state).ToList();
				}
			}
			return Alices.Where(x => x.State != state).ToList();
		}

		public void StartAliceTimeout(Guid uniqueId)
		{
			// 1. Find Alice and set its LastSeen property.
			var foundAlice = false;
			var started = DateTimeOffset.UtcNow;
			using (RoundSynchronizerLock.Lock())
			{
				if (Phase != RoundPhase.InputRegistration || Status != CoordinatorRoundStatus.Running)
				{
					return; // Then no need to timeout alice.
				}

				Alice alice = Alices.SingleOrDefault(x => x.UniqueId == uniqueId);
				foundAlice = alice is { };
				if (foundAlice)
				{
					alice.LastSeen = started;
				}
			}

			if (foundAlice)
			{
				Task.Run(async () =>
				{
					// 2. Delay asynchronously to the requested timeout
					await Task.Delay(AliceRegistrationTimeout).ConfigureAwait(false);

					using (await RoundSynchronizerLock.LockAsync().ConfigureAwait(false))
					{
						// 3. If the round is still running and the phase is still InputRegistration
						if (Status == CoordinatorRoundStatus.Running && Phase == RoundPhase.InputRegistration)
						{
							Alice alice = Alices.SingleOrDefault(x => x.UniqueId == uniqueId);
							if (alice is { })
							{
								// 4. If LastSeen is not changed by then, remove Alice.
								// But only if Alice didn't get blind sig yet.
								if (alice.LastSeen == started && alice.State < AliceState.ConnectionConfirmed)
								{
									Alices.Remove(alice);
									Logger.LogInfo($"Round ({RoundId}): Alice ({alice.UniqueId}) timed out.");
								}
							}
						}
					}
				});
			}
		}

		public static int AdjustConfirmationTarget(int unconfirmedCount, int startingConfirmationTarget, double confirmationTargetReductionRate)
		{
			for (int i = 0; i < unconfirmedCount; i++)
			{
				startingConfirmationTarget = (int)(startingConfirmationTarget * confirmationTargetReductionRate);
			}

			startingConfirmationTarget = Math.Max(startingConfirmationTarget, 2); // Conf target should never be less than 2.
			return startingConfirmationTarget;
		}

		#region Modifiers

		public async Task BroadcastCoinJoinIfFullySignedAsync()
		{
			Transaction broadcasted = null;
			using (await RoundSynchronizerLock.LockAsync().ConfigureAwait(false))
			{
				// Check if fully signed.
				if (CoinJoin.Inputs.All(x => x.HasWitScript()))
				{
					Logger.LogInfo($"Round ({RoundId}): Trying to broadcast coinjoin.");

					try
					{
						Coin[] spentCoins = Alices.SelectMany(x => x.Inputs).ToArray();
						Money networkFee = CoinJoin.GetFee(spentCoins);
						Logger.LogInfo($"Round ({RoundId}): Network Fee: {networkFee.ToString(false, false)} BTC.");
						FeeRate feeRate = CoinJoin.GetFeeRate(spentCoins);
						Logger.LogInfo($"Round ({RoundId}): Network Fee Rate: {feeRate.FeePerK.ToDecimal(MoneyUnit.Satoshi) / 1000} sat/vByte.");
						Logger.LogInfo($"Round ({RoundId}): Number of inputs: {CoinJoin.Inputs.Count}.");
						Logger.LogInfo($"Round ({RoundId}): Number of outputs: {CoinJoin.Outputs.Count}.");
						Logger.LogInfo($"Round ({RoundId}): Serialized Size: {CoinJoin.GetSerializedSize() / 1024} KB.");
						Logger.LogInfo($"Round ({RoundId}): VSize: {CoinJoin.GetVirtualSize() / 1024} KB.");
						foreach (var (value, count) in CoinJoin.GetIndistinguishableOutputs(includeSingle: false))
						{
							Logger.LogInfo($"Round ({RoundId}): There are {count} occurrences of {value.ToString(true, false)} BTC output.");
						}

						await RpcClient.SendRawTransactionAsync(CoinJoin).ConfigureAwait(false);
						broadcasted = CoinJoin;
						Succeed(syncLock: false);
						Logger.LogInfo($"Round ({RoundId}): Successfully broadcasted the CoinJoin: {CoinJoin.GetHash()}.");
					}
					catch (Exception ex)
					{
						Abort($"Could not broadcast the CoinJoin: {CoinJoin.GetHash()}.", syncLock: false);
						Logger.LogError(ex);
					}
				}
			}

			if (broadcasted is { })
			{
				CoinJoinBroadcasted?.Invoke(this, broadcasted);
			}
		}

		public void UpdateAnonymitySet(int anonymitySet, bool syncLock = true)
		{
			if (syncLock)
			{
				using (RoundSynchronizerLock.Lock())
				{
					if ((Phase != RoundPhase.InputRegistration && Phase != RoundPhase.ConnectionConfirmation) || Status != CoordinatorRoundStatus.Running)
					{
						throw new InvalidOperationException($"Updating anonymity set is not allowed in {Phase} phase.");
					}
					AnonymitySet = anonymitySet;
				}
			}
			else
			{
				if ((Phase != RoundPhase.InputRegistration && Phase != RoundPhase.ConnectionConfirmation) || Status != CoordinatorRoundStatus.Running)
				{
					throw new InvalidOperationException($"Updating anonymity set is not allowed in {Phase} phase.");
				}
				AnonymitySet = anonymitySet;
			}
			Logger.LogInfo($"Round ({RoundId}): {nameof(AnonymitySet)} updated: {AnonymitySet}.");
		}

		public void AddAlice(Alice alice)
		{
			using (RoundSynchronizerLock.Lock())
			{
				if (Phase != RoundPhase.InputRegistration || Status != CoordinatorRoundStatus.Running)
				{
					throw new InvalidOperationException("Adding Alice is only allowed in InputRegistration phase.");
				}
				Alices.Add(alice);
			}

			StartAliceTimeout(alice.UniqueId);

			Logger.LogDebug($"Round ({RoundId}): Alice ({alice.InputSum.ToString(false, false)}) added.");
		}

		public void AddBob(Bob bob)
		{
			using (RoundSynchronizerLock.Lock())
			{
				if (Phase != RoundPhase.OutputRegistration || Status != CoordinatorRoundStatus.Running)
				{
					throw new InvalidOperationException("Adding Bob is only allowed in OutputRegistration phase.");
				}

				// If Bob is already added with the same scriptpubkey and level, that's fine.
				Bobs.Add(bob);
			}

			Logger.LogDebug($"Round ({RoundId}): Bob ({bob.Level.Denomination}) added.");
		}

		public async Task RemoveAlicesIfAnInputRefusedByMempoolAsync()
		{
			using (await RoundSynchronizerLock.LockAsync().ConfigureAwait(false))
			{
				await RemoveAlicesIfAnInputRefusedByMempoolNoLockAsync().ConfigureAwait(false);
			}
		}

		private async Task RemoveAlicesIfAnInputRefusedByMempoolNoLockAsync()
		{
			if (Phase != RoundPhase.InputRegistration || Status != CoordinatorRoundStatus.Running)
			{
				throw new InvalidOperationException($"Round ({RoundId}): Removing Alice is only allowed in {RoundPhase.InputRegistration} phase.");
			}

			// If we can build a transaction that the mempool accepts, then we're good, no need to remove any Alices.
			(bool accept, string rejectReason) resultAll = await TestMempoolAcceptWithTransactionSimulationAsync().ConfigureAwait(false);
			if (!resultAll.accept)
			{
				Logger.LogInfo($"Round ({RoundId}): Mempool acceptance is unsuccessful! Number of Alices: {Alices.Count}.");

				// The created tx was not accepted. Let's figure out why. Is it because an Alice doublespent or because of too long mempool chains.
				var responses = await GetTxOutForAllInputsAsync().ConfigureAwait(false);

				var alicesSpent = new HashSet<Alice>();
				var alicesUnconfirmed = new HashSet<Alice>();
				foreach (var (alice, resp) in responses)
				{
					if (resp is null)
					{
						alicesSpent.Add(alice);
					}
					else if (resp.Confirmations <= 0)
					{
						alicesUnconfirmed.Add(alice);
					}
				}

				// Let's go through Alices those have spent inputs and remove them.
				foreach (var alice in alicesSpent.Where(x => x.State < AliceState.ConnectionConfirmed))
				{
					Alices.Remove(alice);
					Logger.LogInfo($"Round ({RoundId}): Alice ({alice.UniqueId}) removed, because of spent inputs.");
				}

				// If we removed spent Alices, then test mempool acceptance again.
				// If we did not remove spent Alices, then no need to test again, we know it's because of unconfirmed Alices.
				var problemSolved = false;
				if (alicesSpent.Any())
				{
					// Let's test another fake transaction, maybe the problem was spent inputs.
					resultAll = await TestMempoolAcceptWithTransactionSimulationAsync().ConfigureAwait(false);
					if (resultAll.accept)
					{
						problemSolved = true;
					}
					Logger.LogInfo($"Round ({RoundId}): Mempool acceptance is unsuccessful! Number of Alices: {Alices.Count}.");
				}

				if (!problemSolved)
				{
					// Let's go remove the unconfirmed Alices.
					// If there are unconfirmed Alices those are also spent Alices, then we don't need to double remove them.
					foreach (var alice in alicesUnconfirmed.Except(alicesSpent).Where(x => x.State < AliceState.ConnectionConfirmed))
					{
						Alices.Remove(alice);
						Logger.LogInfo($"Round ({RoundId}): Alice ({alice.UniqueId}) removed, because of unconfirmed inputs.");
					}
				}
			}
		}

		private async Task<List<(Alice alice, GetTxOutResponse resp)>> GetTxOutForAllInputsAsync()
		{
			var responses = new List<(Alice alice, GetTxOutResponse resp)>();

			var inputAliceDic = new Dictionary<OutPoint, Alice>();
			foreach (Alice alice in Alices)
			{
				foreach (var input in alice.Inputs.Select(x => x.Outpoint))
				{
					inputAliceDic.Add(input, alice);
				}
			}

			foreach (var dicBatch in inputAliceDic.Batch(8)) // 8 is default rpcworkqueue/2, so other requests can go.
			{
				var checkingTasks = new List<(Alice alice, Task<GetTxOutResponse?> task)>();
				var batch = RpcClient.PrepareBatch();

				foreach (var aliceInput in dicBatch)
				{
					var alice = aliceInput.Value;
					var input = aliceInput.Key;
					checkingTasks.Add((alice, batch.GetTxOutAsync(input.Hash, (int)input.N, includeMempool: true)));
				}

				await batch.SendBatchAsync().ConfigureAwait(false);

				foreach (var (alice, task) in checkingTasks)
				{
					var resp = await task.ConfigureAwait(false);
					responses.Add((alice, resp));
				}
			}

			return responses;
		}

		private async Task<(bool accept, string rejectReason)> TestMempoolAcceptWithTransactionSimulationAsync()
		{
			// Check if mempool would accept a fake transaction created with all the registered inputs.
			var coinsToTest = Alices.SelectMany(alice => alice.Inputs);
			// Add the outputs by denomination level. Add 1 as estimation could be sometimes off by 1.
			var outputCount = Alices.Sum(alice => EstimateBestMixingLevel(alice) + 1);
			// Add the change outputs.
			outputCount += Alices.Count;

			return await RpcClient.TestMempoolAcceptAsync(coinsToTest, fakeOutputCount: outputCount, FeePerInputs, FeePerOutputs).ConfigureAwait(false);
		}

		public int RemoveAlicesBy(params Guid[] ids)
		{
			var numberOfRemovedAlices = 0;
			using (RoundSynchronizerLock.Lock())
			{
				if ((Phase != RoundPhase.InputRegistration && Phase != RoundPhase.ConnectionConfirmation) || Status != CoordinatorRoundStatus.Running)
				{
					throw new InvalidOperationException("Removing Alice is only allowed in InputRegistration and ConnectionConfirmation phases.");
				}
				foreach (var id in ids)
				{
					numberOfRemovedAlices = Alices.RemoveAll(x => x.UniqueId == id && x.State < AliceState.ConnectionConfirmed);
				}
			}

			if (numberOfRemovedAlices > 0)
			{
				Logger.LogInfo($"Round ({RoundId}): {numberOfRemovedAlices} alices are removed.");
			}

			return numberOfRemovedAlices;
		}

		#endregion Modifiers
	}
}
