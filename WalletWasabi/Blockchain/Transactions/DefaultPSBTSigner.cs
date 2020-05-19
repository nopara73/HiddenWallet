using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Interfaces;

namespace WalletWasabi.Blockchain.Transactions
{
	public class DefaultPSBTSigner : IPsbtSigner
	{
		private readonly string _password;
		private readonly SmartCoin[] _spentCoins;
		private readonly TransactionBuilder _transactionBuilder;
		private readonly Func<LockTime> _lockTimeSelector;

		public DefaultPSBTSigner(string password, SmartCoin[] spentCoins, TransactionBuilder transactionBuilder,
			Func<LockTime> lockTimeSelector)
		{
			_password = password;
			_spentCoins = spentCoins;
			_transactionBuilder = transactionBuilder;
			_lockTimeSelector = lockTimeSelector;
		}

		public Task<PSBT> TrySign(PSBT psbt, KeyManager keyManager, CancellationToken cancellationToken)
		{
			if (keyManager.IsWatchOnly)
			{
				return Task.FromResult((PSBT) null);
			}

			var workingPsbt = psbt.Clone();
			IEnumerable<ExtKey> signingKeys =
				keyManager.GetSecrets(_password, _spentCoins.Select(x => x.ScriptPubKey).ToArray());
			var workingTransactionBuilder = _transactionBuilder.AddKeys(signingKeys.ToArray());
			workingTransactionBuilder.SetLockTime(_lockTimeSelector());
			workingTransactionBuilder.SignPSBT(workingPsbt);
			return Task.FromResult(workingPsbt);
		}
	}
}