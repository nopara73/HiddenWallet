using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace WalletWasabi.Wallets
{
	/// <summary>
	/// IBlockProvider is an abstraction for types that can return blocks.
	/// </summary>
	public interface IBlockProvider
	{
		Task<Block?> GetBlockAsync(uint256 hash, CancellationToken cancel);
	}
}
