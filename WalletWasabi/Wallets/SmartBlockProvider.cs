using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace WalletWasabi.Wallets
{
	/// <summary>
	/// <see cref="SmartBlockProvider"/> is a block provider that can provide
	/// blocks from multiple requesters.
	/// </summary>
	public class SmartBlockProvider : IBlockProvider
	{
		public SmartBlockProvider(IBlockProvider provider, IMemoryCache cache)
		{
			InnerBlockProvider = provider;
			Cache = cache;
		}

		private IBlockProvider InnerBlockProvider { get; }

		private IMemoryCache Cache { get; }

		public async Task<Block?> GetBlockAsync(uint256 blockHash, CancellationToken cancel)
		{
			string cacheKey = $"{nameof(SmartBlockProvider)}:{nameof(GetBlockAsync)}:{blockHash}";
			var cacheOptions = new MemoryCacheEntryOptions
			{
				Size = 10,
				SlidingExpiration = TimeSpan.FromSeconds(4)
			};

			return await Cache.AtomicGetOrCreateAsync(
				cacheKey,
				cacheOptions,
				() => InnerBlockProvider.GetBlockAsync(blockHash, cancel)).ConfigureAwait(false);
		}
	}
}
