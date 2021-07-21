using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.WebClients.Wasabi;

namespace WalletWasabi.Wallets
{
	/// <summary>
	/// P2pBlockProvider is a blocks provider that provides blocks
	/// from bitcoin nodes using the P2P bitcoin protocol.
	/// </summary>
	public class P2pBlockProvider : IBlockProvider
	{
		private Node? _localBitcoinCoreNode = null;

		public P2pBlockProvider(NodesGroup nodes, CoreNode? coreNode, HttpClientFactory httpClientFactory, ServiceConfiguration serviceConfiguration, Network network)
		{
			Nodes = nodes;
			CoreNode = coreNode;
			HttpClientFactory = httpClientFactory;
			ServiceConfiguration = serviceConfiguration;
			Network = network;
		}

		public static event EventHandler<bool>? DownloadingBlockChanged;

		public NodesGroup Nodes { get; }
		public CoreNode? CoreNode { get; }
		public HttpClientFactory HttpClientFactory { get; }
		public ServiceConfiguration ServiceConfiguration { get; }
		public Network Network { get; }

		public Node? LocalBitcoinCoreNode
		{
			get
			{
				if (Network == Network.RegTest)
				{
					return Nodes.ConnectedNodes.First();
				}

				return _localBitcoinCoreNode;
			}
			private set => _localBitcoinCoreNode = value;
		}

		private int NodeTimeouts { get; set; }

		/// <summary>
		/// Gets a bitcoin block from bitcoin nodes using the p2p bitcoin protocol.
		/// If a bitcoin node is available it fetches the blocks using the rpc interface.
		/// </summary>
		/// <param name="hash">The block's hash that identifies the requested block.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <returns>The requested bitcoin block.</returns>
		public async Task<Block?> GetBlockAsync(uint256 hash, CancellationToken cancellationToken)
		{
			Block? block = null;
			try
			{
				DownloadingBlockChanged?.Invoke(null, true);

				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						// Try to get block information from local running Core node first.
						block = await TryDownloadBlockFromLocalNodeAsync(hash, cancellationToken).ConfigureAwait(false);

						if (block is { })
						{
							break;
						}

						// If no connection, wait, then continue.
						while (Nodes.ConnectedNodes.Count == 0)
						{
							await Task.Delay(100, cancellationToken).ConfigureAwait(false);
						}

						// Select a random node we are connected to.
						Node? node = Nodes.ConnectedNodes.RandomElement();
						if (node is null || !node.IsConnected)
						{
							await Task.Delay(100, cancellationToken).ConfigureAwait(false);
							continue;
						}

						// Download block from selected node.
						try
						{
							using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RuntimeParams.Instance.NetworkNodeTimeout))) // 1/2 ADSL	512 kbit/s	00:00:32
							{
								using var lts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
								block = await node.DownloadBlockAsync(hash, lts.Token).ConfigureAwait(false);
							}

							// Validate block
							if (!block.Check())
							{
								DisconnectNode(node, $"Disconnected node: {node.RemoteSocketAddress}, because invalid block received.", force: true);
								continue;
							}

							DisconnectNode(node, $"Disconnected node: {node.RemoteSocketAddress}. Block ({block.GetCoinbaseHeight()}) downloaded: {block.GetHash()}.");

							await NodeTimeoutsAsync(false).ConfigureAwait(false);
						}
						catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
						{
							await NodeTimeoutsAsync(true).ConfigureAwait(false);

							DisconnectNode(node, $"Disconnected node: {node.RemoteSocketAddress}, because block download took too long."); // it could be a slow connection and not a misbehaving node
							continue;
						}
						catch (Exception ex)
						{
							Logger.LogDebug(ex);
							DisconnectNode(node,
								$"Disconnected node: {node.RemoteSocketAddress}, because block download failed: {ex.Message}.",
								force: true);
							continue;
						}

						break; // If got this far, then we have the block and it's valid. Break.
					}
					catch (Exception ex)
					{
						Logger.LogDebug(ex);
					}
				}
			}
			finally
			{
				DownloadingBlockChanged?.Invoke(null, false);
			}

			return block;
		}

		private async Task<Block?> TryDownloadBlockFromLocalNodeAsync(uint256 hash, CancellationToken cancellationToken)
		{
			if (CoreNode?.RpcClient is null)
			{
				try
				{
					if (LocalBitcoinCoreNode is null || (!LocalBitcoinCoreNode.IsConnected && Network != Network.RegTest)) // If RegTest then we're already connected do not try again.
					{
						DisconnectDisposeNullLocalBitcoinCoreNode();
						using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
						handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
						var nodeConnectionParameters = new NodeConnectionParameters()
						{
							ConnectCancellation = handshakeTimeout.Token,
							IsRelay = false,
							UserAgent = $"/Wasabi:{Constants.ClientVersion}/"
						};

						// If an onion was added must try to use Tor.
						// onlyForOnionHosts should connect to it if it's an onion endpoint automatically and non-Tor endpoints through clearnet/localhost
						if (HttpClientFactory.IsTorEnabled)
						{
							nodeConnectionParameters.TemplateBehaviors.Add(new SocksSettingsBehavior(HttpClientFactory.TorEndpoint, onlyForOnionHosts: true, networkCredential: null, streamIsolation: false));
						}

						var localEndPoint = ServiceConfiguration.BitcoinCoreEndPoint;
						var localNode = await Node.ConnectAsync(Network, localEndPoint, nodeConnectionParameters).ConfigureAwait(false);
						try
						{
							Logger.LogInfo("TCP Connection succeeded, handshaking...");
							localNode.VersionHandshake(Constants.LocalNodeRequirements, handshakeTimeout.Token);
							var peerServices = localNode.PeerVersion.Services;

							Logger.LogInfo("Handshake completed successfully.");

							if (!localNode.IsConnected)
							{
								throw new InvalidOperationException($"Wasabi could not complete the handshake with the local node and dropped the connection.{Environment.NewLine}" +
									"Probably this is because the node does not support retrieving full blocks or segwit serialization.");
							}
							LocalBitcoinCoreNode = localNode;
						}
						catch (OperationCanceledException) when (handshakeTimeout.IsCancellationRequested)
						{
							Logger.LogWarning($"Wasabi could not complete the handshake with the local node. Probably Wasabi is not whitelisted by the node.{Environment.NewLine}" +
								"Use \"whitebind\" in the node configuration. (Typically whitebind=127.0.0.1:8333 if Wasabi and the node are on the same machine and whitelist=1.2.3.4 if they are not.)");
							throw;
						}
					}

					// Get Block from local node
					Block blockFromLocalNode;
					// Should timeout faster. Not sure if it should ever fail though. Maybe let's keep like this later for remote node connection.
					using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(64)))
					{
						blockFromLocalNode = await LocalBitcoinCoreNode.DownloadBlockAsync(hash, cts.Token).ConfigureAwait(false);
					}

					// Validate retrieved block
					if (!blockFromLocalNode.Check())
					{
						throw new InvalidOperationException("Disconnected node, because invalid block received!");
					}

					// Retrieved block from local node and block is valid
					Logger.LogInfo($"Block acquired from local P2P connection: {hash}.");
					return blockFromLocalNode;
				}
				catch (Exception ex)
				{
					DisconnectDisposeNullLocalBitcoinCoreNode();

					if (ex is SocketException)
					{
						Logger.LogTrace("Did not find local listening and running full node instance. Trying to fetch needed block from other source.");
					}
					else
					{
						Logger.LogWarning(ex);
					}
				}
			}
			else
			{
				try
				{
					var block = await CoreNode.RpcClient.GetBlockAsync(hash).ConfigureAwait(false);
					Logger.LogInfo($"Block acquired from RPC connection: {hash}.");
					return block;
				}
				catch (Exception ex)
				{
					Logger.LogWarning(ex);
				}
			}

			return null;
		}

		private void DisconnectDisposeNullLocalBitcoinCoreNode()
		{
			if (LocalBitcoinCoreNode is { })
			{
				try
				{
					LocalBitcoinCoreNode?.Disconnect();
				}
				catch (Exception ex)
				{
					Logger.LogDebug(ex);
				}
				finally
				{
					try
					{
						LocalBitcoinCoreNode?.Dispose();
					}
					catch (Exception ex)
					{
						Logger.LogDebug(ex);
					}
					finally
					{
						LocalBitcoinCoreNode = null;
						Logger.LogInfo($"Local {Constants.BuiltinBitcoinNodeName} node disconnected.");
					}
				}
			}
		}

		private void DisconnectNode(Node node, string logIfDisconnect, bool force = false)
		{
			if (Nodes.ConnectedNodes.Count > 3 || force)
			{
				Logger.LogInfo(logIfDisconnect);
				node.DisconnectAsync(logIfDisconnect);
			}
		}

		/// <summary>
		/// Current timeout used when downloading a block from the remote node. It is defined in seconds.
		/// </summary>
		private async Task NodeTimeoutsAsync(bool increaseDecrease)
		{
			if (increaseDecrease)
			{
				NodeTimeouts++;
			}
			else
			{
				NodeTimeouts--;
			}

			var timeout = RuntimeParams.Instance.NetworkNodeTimeout;

			// If it times out 2 times in a row then increase the timeout.
			if (NodeTimeouts >= 2)
			{
				NodeTimeouts = 0;
				timeout *= 2;
			}
			else if (NodeTimeouts <= -3) // If it does not time out 3 times in a row, lower the timeout.
			{
				NodeTimeouts = 0;
				timeout = (int)Math.Round(timeout * 0.7);
			}

			// Sanity check
			if (timeout < 32)
			{
				timeout = 32;
			}
			else if (timeout > 600)
			{
				timeout = 600;
			}

			if (timeout == RuntimeParams.Instance.NetworkNodeTimeout)
			{
				return;
			}

			RuntimeParams.Instance.NetworkNodeTimeout = timeout;
			await RuntimeParams.Instance.SaveAsync().ConfigureAwait(false);

			Logger.LogInfo($"Current timeout value used on block download is: {timeout} seconds.");
		}
	}
}
