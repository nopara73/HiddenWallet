using Moq;
using NBitcoin;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Crypto;
using WalletWasabi.Models;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.Wallets;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Client
{
	public class OwnershipProofValidatorTests
	{
		[Fact]
		public async Task CoinsAlreadySeenTestAsync()
		{
			// We need the `TransactionStore` instance to verify coins created in transactions (coinjoins by sure)
			// that are saved in our disk (because they are relevant for us). The `BlockProvider` is used to get
			// the blocks containing the transactions that create the coins that we have to verify.
			await using var transactionStore = await CreateTransactionStoreAsync().ConfigureAwait(false);
			var blockProvider = new Mock<IBlockProvider>();

			using var otherAliceKey = new Key();
			var scriptPubKey = BitcoinFactory.CreateScript(otherAliceKey);
			var tx = CreateCreditingTransaction(scriptPubKey, Money.Coins(0.1234m));

			// Store the transaction in the store.
			transactionStore.TryAddOrUpdate(tx);

			var roundId = uint256.Zero;
			var proof = OwnershipProof.GenerateCoinJoinInputProof(
				otherAliceKey,
				new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", roundId));

			var validator = new OwnershipProofValidator(transactionStore, blockProvider.Object);

			// Verify the ownership proof is valid.
			var dummyBlockId = uint256.Zero;
			var coin = tx.Transaction.Outputs.AsCoins().First();
			var validProofs = await validator.VerifyOtherAlicesOwnershipProofsAsync(
				roundId,
				new[] { (dummyBlockId, coin.Outpoint, coin.Amount, proof) },
				10,
				CancellationToken.None);
			Assert.Equal(1, validProofs);

			// Verify the ownership proof is valid (non-existing one).
			coin.Outpoint.N = 10; // non-existing coin
			await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.VerifyOtherAlicesOwnershipProofsAsync(
				roundId,
				new[] { (dummyBlockId, coin.Outpoint, coin.Amount, proof) },
				10,
				CancellationToken.None));

			// Verify the ownership proof is valid (non-existing one).
			coin.Outpoint = BitcoinFactory.CreateOutPoint(); // non-existing transaction
			validProofs = await validator.VerifyOtherAlicesOwnershipProofsAsync(
				roundId,
				new[] { (dummyBlockId, coin.Outpoint, coin.Amount, proof) },
				10,
				CancellationToken.None);

			Assert.Equal(0, validProofs);
		}

		[Fact]
		public async Task CoinsNeverSeenBeforeTestAsync()
		{
			await using var transactionStore = await CreateTransactionStoreAsync().ConfigureAwait(false);
			var blockProvider = new Mock<IBlockProvider>();

			using var otherAliceKey = new Key();
			var scriptPubKey = BitcoinFactory.CreateScript(otherAliceKey);
			var stx = CreateCreditingTransaction(scriptPubKey, Money.Coins(0.1234m));

			// put the transaction in a block.
			var block = Block.CreateBlock(Network.Main);
			block.AddTransaction(stx.Transaction);
			var blockId = block.GetHash();
			blockProvider.Setup(x => x.GetBlockAsync(blockId, It.IsAny<CancellationToken>())).ReturnsAsync(block);

			var roundId = uint256.Zero;
			var proof = OwnershipProof.GenerateCoinJoinInputProof(
				otherAliceKey,
				new CoinJoinInputCommitmentData("CoinJoinCoordinatorIdentifier", roundId));

			// verify the proof is valid.
			var validCoin = stx.Transaction.Outputs.AsCoins().First();
			var validator = new OwnershipProofValidator(transactionStore, blockProvider.Object);
			var validProofs = await validator.VerifyOtherAlicesOwnershipProofsAsync(
				roundId,
				new[] { (blockId, validCoin.Outpoint, validCoin.Amount, proof) },
				10,
				CancellationToken.None);
			Assert.Equal(1, validProofs);

			// validate a coin comming from a non-existing transaction.
			await Assert.ThrowsAsync<InvalidOperationException>(async () => await validator.VerifyOtherAlicesOwnershipProofsAsync(
				roundId,
				new[] { (blockId, BitcoinFactory.CreateOutPoint(), Money.Coins(8.118736401m), proof) },
				10,
				CancellationToken.None));

			// validate a coin comming from an non-existing block.
			validProofs = await validator.VerifyOtherAlicesOwnershipProofsAsync(
				roundId,
				new[] { (uint256.One, validCoin.Outpoint, validCoin.Amount, proof) },
				10,
				CancellationToken.None);

			Assert.Equal(0, validProofs);
		}

		private async Task<TransactionStore> CreateTransactionStoreAsync([CallerFilePath] string callerFilePath = "", [CallerMemberName] string callerMemberName = "")
		{
			string dir = Path.Combine(Common.GetWorkDir(callerFilePath, callerMemberName), "TransactionStore");
			await IoHelpers.TryDeleteDirectoryAsync(dir);
			var txStore = new TransactionStore();
			await txStore.InitializeAsync(dir, Network.Main, "", CancellationToken.None);
			return txStore;
		}

		private static SmartTransaction CreateCreditingTransaction(Script scriptPubKey, Money amount, int height = 0)
		{
			var tx = Network.RegTest.CreateTransaction();
			tx.Version = 1;
			tx.LockTime = LockTime.Zero;
			tx.Inputs.Add(BitcoinFactory.CreateOutPoint(), new Script(OpcodeType.OP_0, OpcodeType.OP_0), sequence: Sequence.Final);
			tx.Inputs.Add(BitcoinFactory.CreateOutPoint(), new Script(OpcodeType.OP_0, OpcodeType.OP_0), sequence: Sequence.Final);
			tx.Outputs.Add(amount, scriptPubKey);
			return new SmartTransaction(tx, height == 0 ? Height.Mempool : new Height(height));
		}
	}
}
