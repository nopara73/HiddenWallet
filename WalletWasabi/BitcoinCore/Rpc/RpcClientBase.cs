using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc.Models;
using WalletWasabi.Helpers;

namespace WalletWasabi.BitcoinCore.Rpc
{
	public class RpcClientBase : IRPCClient
	{
		public RpcClientBase(RPCClient rpc)
		{
			Rpc = Guard.NotNull(nameof(rpc), rpc);
		}

		public Network Network => Rpc.Network;

		private RPCClient Rpc { get; }

		public RPCCredentialString CredentialString => Rpc.CredentialString;

		public virtual async Task<uint256> GetBestBlockHashAsync()
		{
			return await Rpc.GetBestBlockHashAsync().ConfigureAwait(false);
		}

		public virtual async Task<Block> GetBlockAsync(uint256 blockHash)
		{
			return await Rpc.GetBlockAsync(blockHash).ConfigureAwait(false);
		}

		public virtual async Task<Block> GetBlockAsync(uint blockHeight)
		{
			return await Rpc.GetBlockAsync(blockHeight).ConfigureAwait(false);
		}

		public virtual async Task<BlockHeader> GetBlockHeaderAsync(uint256 blockHash)
		{
			return await Rpc.GetBlockHeaderAsync(blockHash).ConfigureAwait(false);
		}

		public virtual async Task<BlockchainInfo> GetBlockchainInfoAsync()
		{
			return await Rpc.GetBlockchainInfoAsync().ConfigureAwait(false);
		}

		public virtual async Task<PeerInfo[]> GetPeersInfoAsync()
		{
			return await Rpc.GetPeersInfoAsync().ConfigureAwait(false);
		}

		public virtual async Task<MempoolEntry> GetMempoolEntryAsync(uint256 txid, bool throwIfNotFound = true)
		{
			return await Rpc.GetMempoolEntryAsync(txid, throwIfNotFound).ConfigureAwait(false);
		}

		public virtual async Task<MemPoolInfo> GetMempoolInfoAsync(CancellationToken cancel = default)
		{
			try
			{
				var response = await Rpc.SendCommandAsync(RPCOperations.getmempoolinfo, true).ConfigureAwait(false);
				static IEnumerable<FeeRateGroup> ExtractFeeRateGroups(JToken jt) =>
					jt switch
					{
						JObject jo => jo.Properties()
							.Where(p => p.Name != "total_fees")
							.Select(p => new FeeRateGroup
							{
								Group = int.Parse(p.Name),
								Sizes = p.Value.Value<ulong>("sizes"),
								Count = p.Value.Value<uint>("count"),
								Fees = Money.Satoshis(p.Value.Value<ulong>("fees")),
								From = new FeeRate(p.Value.Value<decimal>("from_feerate")),
								To = new FeeRate(Math.Min(50_000, p.Value.Value<decimal>("to_feerate")))
							}),
						_ => Enumerable.Empty<FeeRateGroup>()
					};

				return new MemPoolInfo()
				{
					Size = int.Parse((string)response.Result["size"], CultureInfo.InvariantCulture),
					Bytes = int.Parse((string)response.Result["bytes"], CultureInfo.InvariantCulture),
					Usage = int.Parse((string)response.Result["usage"], CultureInfo.InvariantCulture),
					MaxMemPool = double.Parse((string)response.Result["maxmempool"], CultureInfo.InvariantCulture),
					MemPoolMinFee = double.Parse((string)response.Result["mempoolminfee"], CultureInfo.InvariantCulture),
					MinRelayTxFee = double.Parse((string)response.Result["minrelaytxfee"], CultureInfo.InvariantCulture),
					Histogram = ExtractFeeRateGroups(response.Result["fee_histogram"]).ToArray()
				};
			}
			catch (RPCException ex) when (ex.RPCCode == RPCErrorCode.RPC_MISC_ERROR)
			{
				cancel.ThrowIfCancellationRequested();

				return await Rpc.GetMemPoolAsync().ConfigureAwait(false);
			}
		}

		public virtual async Task<uint256[]> GetRawMempoolAsync()
		{
			return await Rpc.GetRawMempoolAsync().ConfigureAwait(false);
		}

		public virtual async Task<GetTxOutResponse?> GetTxOutAsync(uint256 txid, int index, bool includeMempool = true)
		{
			return await Rpc.GetTxOutAsync(txid, index, includeMempool).ConfigureAwait(false);
		}

		public virtual async Task<MempoolAcceptResult> TestMempoolAcceptAsync(Transaction transaction, bool allowHighFees = false)
		{
			return await Rpc.TestMempoolAcceptAsync(transaction, allowHighFees).ConfigureAwait(false);
		}

		public virtual async Task StopAsync()
		{
			await Rpc.StopAsync().ConfigureAwait(false);
		}

		public virtual async Task<uint256[]> GenerateAsync(int blockCount)
		{
			return await Rpc.GenerateAsync(blockCount).ConfigureAwait(false);
		}

		public virtual async Task<TimeSpan> UptimeAsync()
		{
			return await Rpc.UptimeAsync().ConfigureAwait(false);
		}

		public virtual async Task<uint256> SendRawTransactionAsync(Transaction transaction)
		{
			return await Rpc.SendRawTransactionAsync(transaction).ConfigureAwait(false);
		}

		public virtual async Task<EstimateSmartFeeResponse> EstimateSmartFeeAsync(int confirmationTarget, EstimateSmartFeeMode estimateMode = EstimateSmartFeeMode.Conservative)
		{
			return await Rpc.EstimateSmartFeeAsync(confirmationTarget, estimateMode).ConfigureAwait(false);
		}

		public virtual IRPCClient PrepareBatch()
		{
			return new RpcClientBase(Rpc.PrepareBatch());
		}

		public async Task<VerboseBlockInfo> GetVerboseBlockAsync(uint256 blockId)
		{
			var resp = await Rpc.SendCommandAsync(RPCOperations.getblock, blockId, 3).ConfigureAwait(false);
			return RpcParser.ParseVerboseBlockResponse(resp.ResultString);
		}

		public async Task<uint256[]> GenerateToAddressAsync(int nBlocks, BitcoinAddress address)
		{
			return await Rpc.GenerateToAddressAsync(nBlocks, address).ConfigureAwait(false);
		}

		#region For Testing Only

		public virtual async Task<uint256> SendToAddressAsync(BitcoinAddress address, Money amount, string? commentTx = null, string? commentDest = null, bool subtractFeeFromAmount = false, bool replaceable = false)
		{
			return await Rpc.SendToAddressAsync(address, amount, commentTx, commentDest, subtractFeeFromAmount, replaceable).ConfigureAwait(false);
		}

		public virtual async Task<uint256> GetBlockHashAsync(int height)
		{
			return await Rpc.GetBlockHashAsync(height).ConfigureAwait(false);
		}

		public virtual async Task InvalidateBlockAsync(uint256 blockHash)
		{
			await Rpc.InvalidateBlockAsync(blockHash).ConfigureAwait(false);
		}

		public virtual async Task AbandonTransactionAsync(uint256 txid)
		{
			await Rpc.AbandonTransactionAsync(txid).ConfigureAwait(false);
		}

		public virtual async Task<BumpResponse> BumpFeeAsync(uint256 txid)
		{
			return await Rpc.BumpFeeAsync(txid).ConfigureAwait(false);
		}

		public virtual async Task<Transaction> GetRawTransactionAsync(uint256 txid, bool throwIfNotFound = true)
		{
			return await Rpc.GetRawTransactionAsync(txid, throwIfNotFound).ConfigureAwait(false);
		}

		public virtual async Task<IEnumerable<Transaction>> GetRawTransactionsAsync(IEnumerable<uint256> txids, CancellationToken cancel)
		{
			// 8 is half of the default rpcworkqueue
			List<Transaction> acquiredTransactions = new();
			foreach (var txidsChunk in txids.ChunkBy(8))
			{
				IRPCClient batchingRpc = PrepareBatch();
				List<Task<Transaction>> tasks = new();
				foreach (var txid in txidsChunk)
				{
					tasks.Add(batchingRpc.GetRawTransactionAsync(txid, throwIfNotFound: false));
				}

				await batchingRpc.SendBatchAsync().ConfigureAwait(false);

				foreach (var tx in await Task.WhenAll(tasks).ConfigureAwait(false))
				{
					if (tx is not null)
					{
						acquiredTransactions.Add(tx);
					}
					cancel.ThrowIfCancellationRequested();
				}
			}

			return acquiredTransactions;
		}

		public virtual async Task<int> GetBlockCountAsync()
		{
			return await Rpc.GetBlockCountAsync().ConfigureAwait(false);
		}

		public virtual async Task<BitcoinAddress> GetNewAddressAsync()
		{
			return await Rpc.GetNewAddressAsync().ConfigureAwait(false);
		}

		public virtual async Task<SignRawTransactionResponse> SignRawTransactionWithWalletAsync(SignRawTransactionRequest request)
		{
			return await Rpc.SignRawTransactionWithWalletAsync(request).ConfigureAwait(false);
		}

		public virtual async Task<UnspentCoin[]> ListUnspentAsync()
		{
			return await Rpc.ListUnspentAsync().ConfigureAwait(false);
		}

		public virtual async Task SendBatchAsync()
		{
			await Rpc.SendBatchAsync().ConfigureAwait(false);
		}

		public Task<EstimateSmartFeeResponse> TryEstimateSmartFeeAsync(int confirmationTarget, EstimateSmartFeeMode estimateMode = EstimateSmartFeeMode.Conservative)
		{
			return Rpc.TryEstimateSmartFeeAsync(confirmationTarget, estimateMode: estimateMode);
		}

		public Task<RPCClient> CreateWalletAsync(string walletNameOrPath, CreateWalletOptions? options = null)
		{
			return Rpc.CreateWalletAsync(walletNameOrPath, options);
		}

		#endregion For Testing Only
	}
}
