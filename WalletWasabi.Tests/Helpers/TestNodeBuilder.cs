using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore;
using WalletWasabi.BitcoinCore.Endpointing;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Helpers;

namespace WalletWasabi.Tests.Helpers
{
	public static class TestNodeBuilder
	{
		public static async Task<CoreNode> CreateAsync([CallerFilePath] string callerFilePath = "", [CallerMemberName] string callerMemberName = "", string additionalFolder = "", MempoolService? mempoolService = null)
		{
			var network = Network.RegTest;
			var nodeParameters = new CoreNodeParams(
					network,
					mempoolService ?? new MempoolService(),
					Path.Combine(Common.GetWorkDir(callerFilePath, callerMemberName), additionalFolder),
					tryRestart: true,
					tryDeleteDataDir: true,
					EndPointStrategy.Random,
					EndPointStrategy.Random,
					txIndex: 1,
					prune: 0,
					mempoolReplacement: "fee,optin",
					userAgent: $"/WasabiClient:{Constants.ClientVersion}/",
					fallbackFee: Money.Coins(0.0002m), // https://github.com/bitcoin/bitcoin/pull/16524
					new MemoryCache(new MemoryCacheOptions()));
			nodeParameters.ListenOnion = 0;
			nodeParameters.Discover = 0;
			nodeParameters.DnsSeed = 0;
			nodeParameters.FixedSeeds = 0;
			nodeParameters.Upnp = 0;
			nodeParameters.NatPmp = 0;
			nodeParameters.PersistMempool = 0;
			return await CoreNode.CreateAsync(
				nodeParameters,
				CancellationToken.None);
		}
	}
}
