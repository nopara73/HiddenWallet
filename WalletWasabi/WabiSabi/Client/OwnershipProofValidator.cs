using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Crypto;
using WalletWasabi.Logging;
using WalletWasabi.Stores;
using WalletWasabi.Wallets;

namespace WalletWasabi.WabiSabi.Client
{
	// OwnershipProofValidator validates the ownership proofs provided by the coordinator.
	public class OwnershipProofValidator
	{
		public OwnershipProofValidator(TransactionStore transactionStore, IBlockProvider blockProvider)
		{
			TransactionStore = transactionStore;
			BlockProvider = blockProvider;
		}

		public TransactionStore TransactionStore { get; }
		public IBlockProvider BlockProvider { get; }

		// Verifies the coins and ownership proofs. These proofs are provided by the coordinator who should
		// have validated them before which means that when dealing with an honest coordinator all these proofs
		// are valid.
		//
		// In case one proof is invalid we have evidence enough to know the coordinator is malicious and we
		// have to abort the process, notify the user and ban the coordinator.
		public async ValueTask<int> VerifyOtherAlicesOwnershipProofsAsync(
			uint256 roundId,
			IEnumerable<(uint256 BlockId, OutPoint OutPoint, Money Amount, OwnershipProof OwnershipProof)> othersCoins,
			int minimumNumberOfValidProofs,
			CancellationToken cancellationToken)
		{
			var validProofs = 0;
			var coinJoinInputCommitmentData = new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", roundId);

			var myConfirmedTxIds = TransactionStore.GetTransactionHashes();
			var alreadySeenCoins = othersCoins.Where(x => myConfirmedTxIds.Contains(x.OutPoint.Hash));

			// In case one Alice is trying to spend an output from a transaction that we already have
			// seen before then we can verify its script without downloading anything from the network.
			foreach (var (_, outpoint, amount, ownershipProof) in alreadySeenCoins)
			{
				if (validProofs >= minimumNumberOfValidProofs)
				{
					break;
				}

				if (TransactionStore.TryGetTransaction(outpoint.Hash, out var stx))
				{
					var transactionCoins = stx.Transaction.Outputs.AsCoins();
					var foundCoin = transactionCoins.ElementAtOrDefault((int)outpoint.N);

					if (foundCoin is not { } coin || coin.Amount != amount)
					{
						throw new InvalidOperationException("The coordinator lies (coin doesn't exist or is different from the one provided by the coordinator).");
					}
					VerifyCoin(foundCoin.ScriptPubKey, coinJoinInputCommitmentData, ownershipProof);
					validProofs++;
				}
			}

			// In case we cannot validate enough proofs using only our already seen transactions, we
			// would need to start downloading the blocks containing the coins that need to be
			// validated.
			var coinsGroupedByBlocks = othersCoins.GroupBy(x => x.BlockId).ToArray();
			coinsGroupedByBlocks.Shuffle();

			foreach (var coinsInBlock in coinsGroupedByBlocks)
			{
				if (validProofs >= minimumNumberOfValidProofs)
				{
					break;
				}

				var blockId = coinsInBlock.Key;
				var block = await BlockProvider.GetBlockAsync(blockId, cancellationToken).ConfigureAwait(false);

				if (block is { })
				{
					foreach (var (_, outpoint, amount, ownershipProof) in coinsInBlock)
					{
						var tx = block.Transactions.FirstOrDefault(x => x.GetHash() == outpoint.Hash);
						if (tx is null)
						{
							throw new InvalidOperationException($"The coordinator lies (there is not transaction with id '{outpoint.Hash}' in block '{blockId}').");
						}
						var transactionCoins = tx.Outputs.AsCoins();
						var foundCoin = transactionCoins.ElementAtOrDefault((int)outpoint.N);
						if (foundCoin is not { } coin || coin.Amount != amount)
						{
							throw new InvalidOperationException("The coordinator lies (coin doesn't exist or is different from the one provided by the coordinator).");
						}
						VerifyCoin(foundCoin.ScriptPubKey, coinJoinInputCommitmentData, ownershipProof);
						validProofs++;
					}
				}
				else
				{
					Logger.LogError($"Block is missing: '{blockId}'");
				}
			}

			return validProofs;
		}

		private static void VerifyCoin(Script scriptPubKey, CoinJoinInputCommitmentData coinJoinInputCommitmentData, OwnershipProof ownershipProof)
		{
			if (!OwnershipProof.VerifyCoinJoinInputProof(ownershipProof, scriptPubKey, coinJoinInputCommitmentData))
			{
				throw new InvalidOperationException("The coordinator lies (the ownership proof is not valid which means Alice cannot really spend it).");
			}
		}
	}
}
