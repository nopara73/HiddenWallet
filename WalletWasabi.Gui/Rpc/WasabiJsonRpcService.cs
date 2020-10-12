using System;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.CoinJoin.Client.Clients.Queuing;
using WalletWasabi.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Gui.Rpc
{
	public partial class WasabiJsonRpcService
	{
		public WasabiJsonRpcService(Global global)
		{
			Global = global;
		}

		private Global Global { get; }
		private Wallet ActiveWallet { get; set; }

		[JsonRpcMethod("listunspentcoins")]
		public object[] GetUnspentCoinList()
		{
			AssertWalletIsLoaded();
			var serverTipHeight = ActiveWallet.BitcoinStore.SmartHeaderChain.ServerTipHeight;
			return ActiveWallet.Coins.Where(x => x.Unspent).Select(x => new
			{
				txid = x.TransactionId.ToString(),
				index = x.Index,
				amount = x.Amount.Satoshi,
				anonymitySet = x.AnonymitySet,
				confirmed = x.Confirmed,
				confirmations = x.Confirmed ? serverTipHeight - (uint)x.Height.Value + 1 : 0,
				label = x.Label.ToString(),
				keyPath = x.HdPubKey.FullKeyPath.ToString(),
				address = x.HdPubKey.GetP2wpkhAddress(Global.Network).ToString()
			}).ToArray();
		}

		[JsonRpcMethod("createwallet")]
		public object CreateWallet(string walletName, string password)
		{
			var walletGenerator = new WalletGenerator(Global.WalletManager.WalletDirectories.WalletsDir, Global.Network);
			walletGenerator.TipHeight = Global.BitcoinStore.SmartHeaderChain.TipHeight;
			var (keyManager, mnemonic) = walletGenerator.GenerateWallet(walletName, password);
			Global.WalletManager.AddWallet(keyManager);
			return mnemonic.ToString();
		}

		[JsonRpcMethod("getwalletinfo")]
		public object WalletInfo()
		{
			AssertWalletIsLoaded();
			var km = ActiveWallet.KeyManager;
			return new
			{
				walletName = ActiveWallet.WalletName,
				walletFile = km.FilePath,
				State = ActiveWallet.State.ToString(),
				extendedAccountPublicKey = km.ExtPubKey.ToString(Global.Network),
				extendedAccountZpub = km.ExtPubKey.ToZpub(Global.Network),
				accountKeyPath = $"m/{km.AccountKeyPath}",
				masterKeyFingerprint = km.MasterFingerprint?.ToString() ?? "",
				balance = ActiveWallet.Coins
							.Where(c => c.Unspent && !c.SpentAccordingToBackend)
							.Sum(c => c.Amount.Satoshi)
			};
		}

		[JsonRpcMethod("getnewaddress")]
		public object GenerateReceiveAddress(string label)
		{
			AssertWalletIsLoaded();
			label = Guard.NotNullOrEmptyOrWhitespace(nameof(label), label, true);

			var hdkey = ActiveWallet.KeyManager
				.GenerateNewKey(new SmartLabel(label), KeyState.Clean, isInternal: false);
			return new
			{
				address = hdkey.GetP2wpkhAddress(Global.Network).ToString(),
				keyPath = hdkey.FullKeyPath.ToString(),
				label = hdkey.Label,
				publicKey = hdkey.PubKey.ToHex(),
				p2wpkh = hdkey.P2wpkhScript.ToHex()
			};
		}

		[JsonRpcMethod("getstatus")]
		public object GetStatus()
		{
			var sync = Global.Synchronizer;

			return new
			{
				torStatus = sync.TorStatus switch
				{
					TorStatus.NotRunning => "Not running",
					TorStatus.Running => "Running",
					_ => "Turned off"
				},
				backendStatus = sync.BackendStatus == BackendStatus.Connected ? "Connected" : "Disconnected",
				bestBlockchainHeight = sync.BitcoinStore.SmartHeaderChain.TipHeight.ToString(),
				bestBlockchainHash = sync.BitcoinStore.SmartHeaderChain.TipHash.ToString(),
				filtersCount = sync.BitcoinStore.SmartHeaderChain.HashCount,
				filtersLeft = sync.BitcoinStore.SmartHeaderChain.HashesLeft,
				network = sync.Network.Name,
				exchangeRate = sync.UsdExchangeRate,
				peers = Global.Nodes.ConnectedNodes.Select(x => new
				{
					isConnected = x.IsConnected,
					lastSeen = x.LastSeen,
					endpoint = x.Peer.Endpoint.ToString(),
					userAgent = x.PeerVersion.UserAgent,
				}).ToArray(),
			};
		}

		[JsonRpcMethod("build")]
		public string BuildTransaction(PaymentInfo[] payments, OutPoint[] coins, int feeTarget = 0, decimal feeRate = 0, string password = null)
		{
			Guard.NotNull(nameof(payments), payments);
			Guard.NotNull(nameof(coins), coins);
			password = Guard.Correct(password);
			
			var argFeeRate = new FeeRate(feeRate);

			static bool InRange<T>(IComparable<T> val, T min, T max) =>
				val.CompareTo(min) >= 0 && val.CompareTo(max) <= 0;

			var feeStrategy = (feeRate, feeTarget) switch
			{
				(_, 0) when InRange(argFeeRate, Constants.MinRelayFeeRate, Constants.AbsurdlyHighFeeRate) => FeeStrategy.CreateFromFeeRate(argFeeRate),
				(0, _) when InRange(feeTarget, 2, Constants.SevenDaysConfirmationTarget) => FeeStrategy.CreateFromConfirmationTarget(feeTarget),
				_ => throw new ArgumentException("Fee parameters are missing, inconsistent or out of range.")
			};

			AssertWalletIsLoaded();
			var payment = new PaymentIntent(payments.Select(p =>
				new DestinationRequest(p.Sendto.ScriptPubKey, MoneyRequest.Create(p.Amount, p.SubtractFee), new SmartLabel(p.Label))));
			var result = ActiveWallet.BuildTransaction(
				password,
				payment,
				feeStrategy,
				allowUnconfirmed: true,
				allowedInputs: coins);
			var smartTx = result.Transaction;

			return smartTx.Transaction.ToHex();
		}

		[JsonRpcMethod("send")]
		public async Task<object> SendTransactionAsync(PaymentInfo[] payments, OutPoint[] coins, int feeTarget = 0, decimal feeRate = 0, string password = null)
		{
			var txHex = BuildTransaction(payments, coins, feeTarget, feeRate, password);
			var smartTx = new SmartTransaction(Transaction.Parse(txHex, Global.Network), Height.Mempool);

			// dequeue the coins we are going to spend
			var toDequeue = ActiveWallet.Coins
				.Where(x => x.CoinJoinInProgress && coins.Contains(x.OutPoint))
				.ToArray();
			if (toDequeue.Any())
			{
				await ActiveWallet.ChaumianClient.DequeueCoinsFromMixAsync(toDequeue, DequeueReason.TransactionBuilding).ConfigureAwait(false);
			}

			await Global.TransactionBroadcaster.SendTransactionAsync(smartTx).ConfigureAwait(false);
			return new
			{
				txid = smartTx.Transaction.GetHash(),
				tx = txHex
			};
		}

		[JsonRpcMethod("gethistory")]
		public object[] GetHistory()
		{
			AssertWalletIsLoaded();
			var txHistoryBuilder = new TransactionHistoryBuilder(ActiveWallet);
			var summary = txHistoryBuilder.BuildHistorySummary();
			return summary.Select(x => new
			{
				datetime = x.DateTime,
				height = x.Height.Value,
				amount = x.Amount.Satoshi,
				label = x.Label,
				tx = x.TransactionId,
				islikelycoinjoin = x.IsLikelyCoinJoinOutput
			}).ToArray();
		}

		[JsonRpcMethod("listkeys")]
		public object[] GetAllKeys()
		{
			AssertWalletIsLoaded();
			var keys = ActiveWallet.KeyManager.GetKeys(null);
			return keys.Select(x => new
			{
				fullKeyPath = x.FullKeyPath.ToString(),
				@internal = x.IsInternal,
				keyState = x.KeyState,
				label = x.Label.ToString(),
				p2wpkhScript = x.P2wpkhScript.ToString(),
				pubkey = x.PubKey.ToString(),
				pubKeyHash = x.PubKeyHash.ToString(),
				address = x.GetP2wpkhAddress(Global.Network).ToString()
			}).ToArray();
		}

		[JsonRpcMethod("enqueue")]
		public async Task EnqueueForCoinJoinAsync(OutPoint[] coins, string password = null)
		{
			Guard.NotNull(nameof(coins), coins);

			AssertWalletIsLoaded();
			var coinsToMix = ActiveWallet.Coins.Where(x => coins.Any(y => y == x.OutPoint));
			await ActiveWallet.ChaumianClient.QueueCoinsToMixAsync(password, coinsToMix.ToArray()).ConfigureAwait(false);
		}

		[JsonRpcMethod("dequeue")]
		public async Task DequeueForCoinJoinAsync(OutPoint[] coins)
		{
			Guard.NotNull(nameof(coins), coins);

			AssertWalletIsLoaded();
			var coinsToDequeue = ActiveWallet.Coins.Where(x => coins.Any(y => y == x.OutPoint));
			await ActiveWallet.ChaumianClient.DequeueCoinsFromMixAsync(coinsToDequeue, DequeueReason.UserRequested).ConfigureAwait(false);
		}

		[JsonRpcMethod("selectwallet")]
		public void SelectWallet(string walletName)
		{
			walletName = Guard.NotNullOrEmptyOrWhitespace(nameof(walletName), walletName);
			try
			{
				var wallet = Global.WalletManager.GetWalletByName(walletName);

				ActiveWallet = wallet;
				if (wallet.State == WalletState.Uninitialized)
				{
					Global.WalletManager.StartWalletAsync(wallet).ConfigureAwait(false);
				}
			}
			catch (InvalidOperationException) // wallet not found
			{
				throw new Exception($"Wallet '{walletName}' not found.");
			}
		}

		[JsonRpcMethod("stop")]
		public async Task StopAsync()
		{
			await Global.DisposeAsync().ConfigureAwait(false);
		}

		private void AssertWalletIsLoaded()
		{
			if (ActiveWallet is null || ActiveWallet.State != WalletState.Started)
			{
				throw new InvalidOperationException("There is no wallet loaded.");
			}
		}
	}
}
