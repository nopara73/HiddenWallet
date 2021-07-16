using Avalonia.Controls.Notifications;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore;
using WalletWasabi.BitcoinCore.Endpointing;
using WalletWasabi.BitcoinCore.Mempool;
using WalletWasabi.BitcoinCore.Monitoring;
using WalletWasabi.BitcoinP2p;
using WalletWasabi.Blockchain.Analysis.FeesEstimation;
using WalletWasabi.Blockchain.BlockFilters;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.TransactionBroadcasting;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.CoinJoin.Client;
using WalletWasabi.CoinJoin.Client.Clients.Queuing;
using WalletWasabi.Extensions;
using WalletWasabi.Gui.Helpers;
using WalletWasabi.Gui.Models;
using WalletWasabi.Gui.Rpc;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.Services.Terminate;
using WalletWasabi.Stores;
using WalletWasabi.Tor;
using WalletWasabi.Tor.Socks5.Pool.Circuits;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.BlockstreamInfo;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Gui
{
	public class Global
	{
		public const string ThemeBackgroundBrushResourceKey = "ThemeBackgroundBrush";
		public const string ApplicationAccentForegroundBrushResourceKey = "ApplicationAccentForegroundBrush";

		public string DataDir { get; }
		public TorSettings TorSettings { get; }
		public BitcoinStore BitcoinStore { get; }

		/// <summary>
		/// HTTP client factory for communicating with the Wasabi backend.
		/// </summary>
		public HttpClientFactory BackendHttpClientFactory { get; }

		/// <summary>
		/// HTTP client factory for communicating with external third parties.
		/// </summary>
		public HttpClientFactory ExternalHttpClientFactory { get; }

		public LegalChecker LegalChecker { get; private set; }
		public Config Config { get; }
		public WasabiSynchronizer Synchronizer { get; private set; }
		public WalletManager WalletManager { get; }
		public TransactionBroadcaster TransactionBroadcaster { get; set; }
		public CoinJoinProcessor CoinJoinProcessor { get; set; }
		private TorProcessManager? TorManager { get; set; }
		public CoreNode BitcoinCoreNode { get; private set; }
		public HostedServices HostedServices { get; }

		public UiConfig UiConfig { get; }

		public Network Network => Config.Network;

		public MemoryCache Cache { get; private set; }

		public JsonRpcServer? RpcServer { get; private set; }

		private SynchronizationContext? SynchronizationContext { get; }

		public Global(string dataDir, Config config, UiConfig uiConfig, WalletManager walletManager)
		{
			using (BenchmarkLogger.Measure())
			{
				SynchronizationContext = SynchronizationContext.Current;
				StoppingCts = new CancellationTokenSource();
				DataDir = dataDir;
				Config = config;
				UiConfig = uiConfig;
				TorSettings = new TorSettings(DataDir, distributionFolderPath: EnvironmentHelpers.GetFullBaseDirectory(), Config.TerminateTorOnExit, Environment.ProcessId);

				HostedServices = new HostedServices();
				WalletManager = walletManager;

				WalletManager.OnDequeue += WalletManager_OnDequeue;
				WalletManager.WalletRelevantTransactionProcessed += WalletManager_WalletRelevantTransactionProcessed;

				var networkWorkFolderPath = Path.Combine(DataDir, "BitcoinStore", Network.ToString());
				var transactionStore = new AllTransactionStore(networkWorkFolderPath, Network);
				var indexStore = new IndexStore(Path.Combine(networkWorkFolderPath, "IndexStore"), Network, new SmartHeaderChain());
				var mempoolService = new MempoolService();
				var blocks = new FileSystemBlockRepository(Path.Combine(networkWorkFolderPath, "Blocks"), Network);

				BitcoinStore = new BitcoinStore(indexStore, transactionStore, mempoolService, blocks);

				if (Config.UseTor)
				{
					BackendHttpClientFactory = new HttpClientFactory(TorSettings.SocksEndpoint, backendUriGetter: () => Config.GetCurrentBackendUri());
					ExternalHttpClientFactory = new HttpClientFactory(TorSettings.SocksEndpoint, backendUriGetter: null);
				}
				else
				{
					BackendHttpClientFactory = new HttpClientFactory(torEndPoint: null, backendUriGetter: () => Config.GetFallbackBackendUri());
					ExternalHttpClientFactory = new HttpClientFactory(torEndPoint: null, backendUriGetter: null);
				}

				Synchronizer = new WasabiSynchronizer(BitcoinStore, BackendHttpClientFactory);
				LegalChecker = new(DataDir);
				TransactionBroadcaster = new TransactionBroadcaster(Network, BitcoinStore, BackendHttpClientFactory, WalletManager);
			}
		}

		private TaskCompletionSource<bool> InitializationCompleted { get; } = new();

		private bool InitializationStarted { get; set; } = false;

		private CancellationTokenSource StoppingCts { get; }

		public async Task InitializeNoWalletAsync(TerminateService terminateService)
		{
			InitializationStarted = true;
			var cancel = StoppingCts.Token;

			try
			{
				Cache = new MemoryCache(new MemoryCacheOptions
				{
					SizeLimit = 1_000,
					ExpirationScanFrequency = TimeSpan.FromSeconds(30)
				});
				var bstoreInitTask = BitcoinStore.InitializeAsync(cancel);

				HostedServices.Register<UpdateChecker>(new UpdateChecker(TimeSpan.FromMinutes(7), Synchronizer), "Software Update Checker");

				await LegalChecker.InitializeAsync(HostedServices.Get<UpdateChecker>()).ConfigureAwait(false);
				cancel.ThrowIfCancellationRequested();

				HostedServices.Register<SystemAwakeChecker>(new SystemAwakeChecker(WalletManager, SynchronizationContext), "System Awake Checker");

				if (Config.UseTor && Network != Network.RegTest)
				{
					using (BenchmarkLogger.Measure(operationName: "TorProcessManager.Start"))
					{
						TorManager = new TorProcessManager(TorSettings);
						await TorManager.StartAsync(cancel).ConfigureAwait(false);
					}

					Tor.Http.TorHttpClient torHttpClient = BackendHttpClientFactory.NewTorHttpClient(Mode.DefaultCircuit);
					HostedServices.Register<TorMonitor>(new TorMonitor(period: TimeSpan.FromSeconds(3), fallbackBackendUri: Config.GetFallbackBackendUri(), torHttpClient, TorManager), nameof(TorMonitor));
				}

				Logger.LogInfo($"{nameof(TorProcessManager)} is initialized.");

				try
				{
					await bstoreInitTask.ConfigureAwait(false);

					// Make sure that the height of the wallets will not be better than the current height of the filters.
					WalletManager.SetMaxBestHeight(BitcoinStore.IndexStore.SmartHeaderChain.TipHeight);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// If our internal data structures in the Bitcoin Store gets corrupted, then it's better to rescan all the wallets.
					WalletManager.SetMaxBestHeight(SmartHeader.GetStartingHeader(Network).Height);
					throw;
				}

				HostedServices.Register<P2pNetwork>(new P2pNetwork(Network, Config.GetBitcoinP2pEndPoint(), Config.UseTor ? TorSettings.SocksEndpoint : null, Path.Combine(DataDir, "BitcoinP2pNetwork"), BitcoinStore), "Bitcoin P2P Network");

				try
				{
					if (Config.StartLocalBitcoinCoreOnStartup)
					{
						BitcoinCoreNode = await CoreNode
							.CreateAsync(
								new CoreNodeParams(
									Network,
									BitcoinStore.MempoolService,
									Config.LocalBitcoinCoreDataDir,
									tryRestart: false,
									tryDeleteDataDir: false,
									EndPointStrategy.Default(Network, EndPointType.P2p),
									EndPointStrategy.Default(Network, EndPointType.Rpc),
									txIndex: null,
									prune: null,
									mempoolReplacement: "fee,optin",
									userAgent: $"/WasabiClient:{Constants.ClientVersion}/",
									fallbackFee: null, // ToDo: Maybe we should have it, not only for tests?
									Cache),
								cancel)
							.ConfigureAwait(false);

						HostedServices.Register<BlockNotifier>(new BlockNotifier(TimeSpan.FromSeconds(7), BitcoinCoreNode.RpcClient, BitcoinCoreNode.P2pNode), "Block Notifier");
						HostedServices.Register<RpcMonitor>(new RpcMonitor(TimeSpan.FromSeconds(7), BitcoinCoreNode.RpcClient), "RPC Monitor");
						HostedServices.Register<RpcFeeProvider>(new RpcFeeProvider(TimeSpan.FromMinutes(1), BitcoinCoreNode.RpcClient, HostedServices.Get<RpcMonitor>()), "RPC Fee Provider");
						HostedServices.Register<MempoolMirror>(new MempoolMirror(TimeSpan.FromSeconds(21), BitcoinCoreNode.RpcClient, BitcoinCoreNode.P2pNode), "Full Node Mempool Mirror");
					}
				}
				catch (Exception ex)
				{
					Logger.LogError(ex);
				}

				HostedServices.Register<BlockstreamInfoFeeProvider>(new BlockstreamInfoFeeProvider(TimeSpan.FromMinutes(3), new(Network, ExternalHttpClientFactory)) { IsPaused = true }, "Blockstream.info Fee Provider");
				HostedServices.Register<ThirdPartyFeeProvider>(new ThirdPartyFeeProvider(TimeSpan.FromSeconds(1), Synchronizer, HostedServices.Get<BlockstreamInfoFeeProvider>()), "Third Party Fee Provider");
				HostedServices.Register<HybridFeeProvider>(new HybridFeeProvider(HostedServices.Get<ThirdPartyFeeProvider>(), HostedServices.GetOrDefault<RpcFeeProvider>()), "Hybrid Fee Provider");

				await HostedServices.StartAllAsync(cancel).ConfigureAwait(false);

				var requestInterval = Network == Network.RegTest ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(30);
				int maxFiltSyncCount = Network == Network.Main ? 1000 : 10000; // On testnet, filters are empty, so it's faster to query them together

				Synchronizer.Start(requestInterval, maxFiltSyncCount);
				Logger.LogInfo("Start synchronizing filters...");

				TransactionBroadcaster.Initialize(HostedServices.Get<P2pNetwork>().Nodes, BitcoinCoreNode?.RpcClient);
				CoinJoinProcessor = new CoinJoinProcessor(Network, Synchronizer, WalletManager, BitcoinCoreNode?.RpcClient);

				var jsonRpcServerConfig = new JsonRpcServerConfiguration(Config);
				if (jsonRpcServerConfig.IsEnabled)
				{
					RpcServer = new JsonRpcServer(this, jsonRpcServerConfig, terminateService);
					try
					{
						await RpcServer.StartAsync(cancel).ConfigureAwait(false);
					}
					catch (HttpListenerException e)
					{
						Logger.LogWarning($"Failed to start {nameof(JsonRpcServer)} with error: {e.Message}.");
						RpcServer = null;
					}
				}

				var blockProvider = new CachedBlockProvider(
					new SmartBlockProvider(
						new P2pBlockProvider(HostedServices.Get<P2pNetwork>().Nodes, BitcoinCoreNode, BackendHttpClientFactory, Config.ServiceConfiguration, Network),
						Cache),
					BitcoinStore.BlockRepository);

				WalletManager.RegisterServices(BitcoinStore, Synchronizer, Config.ServiceConfiguration, HostedServices.Get<HybridFeeProvider>(), blockProvider);
			}
			finally
			{
				InitializationCompleted.TrySetResult(true);
			}
		}

		private void WalletManager_OnDequeue(object? sender, DequeueResult e)
		{
			try
			{
				if (UiConfig.PrivacyMode)
				{
					return;
				}

				foreach (var success in e.Successful.Where(x => x.Value.Any()))
				{
					DequeueReason reason = success.Key;
					if (reason != DequeueReason.Spent)
					{
						var type = reason == DequeueReason.UserRequested ? NotificationType.Information : NotificationType.Warning;
						var message = reason == DequeueReason.UserRequested ? "" : reason.FriendlyName();
						var title = success.Value.Count() == 1 ? $"Coin ({success.Value.First().Amount.ToString(false, true)}) Dequeued" : $"{success.Value.Count()} Coins Dequeued";
						NotificationHelpers.Notify(message, title, type, sender: sender);
					}
				}

				foreach (var failure in e.Unsuccessful.Where(x => x.Value.Any()))
				{
					DequeueReason reason = failure.Key;
					var type = NotificationType.Warning;
					var message = reason.FriendlyName();
					var title = failure.Value.Count() == 1 ? $"Couldn't Dequeue Coin ({failure.Value.First().Amount.ToString(false, true)})" : $"Couldn't Dequeue {failure.Value.Count()} Coins";
					NotificationHelpers.Notify(message, title, type, sender: sender);
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
		}

		private void WalletManager_WalletRelevantTransactionProcessed(object? sender, ProcessedResult e)
		{
			try
			{
				// In Privacy mode no notification is raised.
				// If there are no news, then don't bother too.
				if (UiConfig.PrivacyMode || !e.IsNews || (sender as Wallet).State != WalletState.Started)
				{
					return;
				}

				// ToDo
				// Double spent.
				// Anonymity set gained?
				// Received dust

				bool isSpent = e.NewlySpentCoins.Any();
				bool isReceived = e.NewlyReceivedCoins.Any();
				bool isConfirmedReceive = e.NewlyConfirmedReceivedCoins.Any();
				bool isConfirmedSpent = e.NewlyConfirmedReceivedCoins.Any();
				Money miningFee = e.Transaction.Transaction.GetFee(e.SpentCoins.Select(x => x.Coin).ToArray());
				if (isReceived || isSpent)
				{
					Money receivedSum = e.NewlyReceivedCoins.Sum(x => x.Amount);
					Money spentSum = e.NewlySpentCoins.Sum(x => x.Amount);
					Money incoming = receivedSum - spentSum;
					Money receiveSpentDiff = incoming.Abs();
					string amountString = receiveSpentDiff.ToString(false, true);

					if (e.Transaction.Transaction.IsCoinBase)
					{
						NotifyAndLog($"{amountString} BTC", "Mined", NotificationType.Success, e, sender);
					}
					else if (isSpent && receiveSpentDiff == miningFee)
					{
						NotifyAndLog($"Mining Fee: {amountString} BTC", "Self Spend", NotificationType.Information, e, sender);
					}
					else if (isSpent && receiveSpentDiff.Almost(Money.Zero, Money.Coins(0.01m)) && e.IsLikelyOwnCoinJoin)
					{
						NotifyAndLog($"CoinJoin Completed!", "", NotificationType.Success, e, sender);
					}
					else if (incoming > Money.Zero)
					{
						if (e.Transaction.IsRBF && e.Transaction.IsReplacement)
						{
							NotifyAndLog($"{amountString} BTC", "Received Replaceable Replacement Transaction", NotificationType.Information, e, sender);
						}
						else if (e.Transaction.IsRBF)
						{
							NotifyAndLog($"{amountString} BTC", "Received Replaceable Transaction", NotificationType.Success, e, sender);
						}
						else if (e.Transaction.IsReplacement)
						{
							NotifyAndLog($"{amountString} BTC", "Received Replacement Transaction", NotificationType.Information, e, sender);
						}
						else
						{
							NotifyAndLog($"{amountString} BTC", "Received", NotificationType.Success, e, sender);
						}
					}
					else if (incoming < Money.Zero)
					{
						NotifyAndLog($"{amountString} BTC", "Sent", NotificationType.Information, e, sender);
					}
				}
				else if (isConfirmedReceive || isConfirmedSpent)
				{
					Money receivedSum = e.ReceivedCoins.Sum(x => x.Amount);
					Money spentSum = e.SpentCoins.Sum(x => x.Amount);
					Money incoming = receivedSum - spentSum;
					Money receiveSpentDiff = incoming.Abs();
					string amountString = receiveSpentDiff.ToString(false, true);

					if (isConfirmedSpent && receiveSpentDiff == miningFee)
					{
						NotifyAndLog($"Mining Fee: {amountString} BTC", "Self Spend Confirmed", NotificationType.Information, e, sender);
					}
					else if (isConfirmedSpent && receiveSpentDiff.Almost(Money.Zero, Money.Coins(0.01m)) && e.IsLikelyOwnCoinJoin)
					{
						NotifyAndLog($"CoinJoin Confirmed!", "", NotificationType.Information, e, sender);
					}
					else if (incoming > Money.Zero)
					{
						NotifyAndLog($"{amountString} BTC", "Receive Confirmed", NotificationType.Information, e, sender);
					}
					else if (incoming < Money.Zero)
					{
						NotifyAndLog($"{amountString} BTC", "Send Confirmed", NotificationType.Information, e, sender);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
		}

		private void NotifyAndLog(string message, string title, NotificationType notificationType, ProcessedResult e, object? sender)
		{
			message = Guard.Correct(message);
			title = Guard.Correct(title);
			NotificationHelpers.Notify(message, title, notificationType, async () => await FileHelpers.OpenFileInTextEditorAsync(Logger.FilePath).ConfigureAwait(false), sender);
			Logger.LogInfo($"Transaction Notification ({notificationType}): {title} - {message} - {e.Transaction.GetHash()}");
		}

		public async Task DisposeAsync()
		{
			Logger.LogWarning("Process is exiting.", nameof(Global));

			try
			{
				StoppingCts?.Cancel();

				if (!InitializationStarted)
				{
					return;
				}

				Logger.LogDebug($"Waiting for initialization to complete.", nameof(Global));

				try
				{
					using var initCompletitionWaitCts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
					await InitializationCompleted.Task.WithAwaitCancellationAsync(initCompletitionWaitCts.Token, 100).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error during wait for initialization to be completed: {ex}");
				}

				try
				{
					using var dequeueCts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
					await WalletManager.RemoveAndStopAllAsync(dequeueCts.Token).ConfigureAwait(false);
					Logger.LogInfo($"{nameof(WalletManager)} is stopped.", nameof(Global));
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error during {nameof(WalletManager.RemoveAndStopAllAsync)}: {ex}");
				}

				WalletManager.OnDequeue -= WalletManager_OnDequeue;
				WalletManager.WalletRelevantTransactionProcessed -= WalletManager_WalletRelevantTransactionProcessed;

				if (RpcServer is { } rpcServer)
				{
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(21));
					await rpcServer.StopAsync(cts.Token).ConfigureAwait(false);
					Logger.LogInfo($"{nameof(RpcServer)} is stopped.", nameof(Global));
				}

				if (CoinJoinProcessor is { } coinJoinProcessor)
				{
					coinJoinProcessor.Dispose();
					Logger.LogInfo($"{nameof(CoinJoinProcessor)} is disposed.");
				}

				if (LegalChecker is { } legalChecker)
				{
					legalChecker.Dispose();
					Logger.LogInfo($"Disposed {nameof(LegalChecker)}.");
				}

				if (HostedServices is { } backgroundServices)
				{
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(21));
					await backgroundServices.StopAllAsync(cts.Token).ConfigureAwait(false);
					backgroundServices.Dispose();
					Logger.LogInfo("Stopped background services.");
				}

				if (Synchronizer is { } synchronizer)
				{
					await synchronizer.StopAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(Synchronizer)} is stopped.");
				}

				if (BackendHttpClientFactory is { } httpClientFactory)
				{
					httpClientFactory.Dispose();
					Logger.LogInfo($"{nameof(BackendHttpClientFactory)} is disposed.");
				}

				if (BitcoinCoreNode is { } bitcoinCoreNode)
				{
					await bitcoinCoreNode.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(BitcoinCoreNode)} is disposed.");

					if (Config.StopLocalBitcoinCoreOnShutdown)
					{
						await bitcoinCoreNode.TryStopAsync().ConfigureAwait(false);
						Logger.LogInfo($"{nameof(BitcoinCoreNode)} is stopped.");
					}
				}

				if (TorManager is { } torManager)
				{
					await torManager.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(TorManager)} is stopped.");
				}

				if (Cache is { } cache)
				{
					cache.Dispose();
					Logger.LogInfo($"{nameof(Cache)} is disposed.");
				}

				try
				{
					await BitcoinStore.DisposeAsync().ConfigureAwait(false);
					Logger.LogInfo($"{nameof(BitcoinStore)} is disposed.");
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error during the disposal of {nameof(BitcoinStore)}: {ex}");
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}
			finally
			{
				StoppingCts?.Dispose();
			}
		}
	}
}
