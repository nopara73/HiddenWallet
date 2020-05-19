using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Keys;

namespace WalletWasabi.Interfaces
{
	public interface IPsbtSigner
	{
		Task<PSBT> TrySign(PSBT psbt, KeyManager keyManager, CancellationToken cancellationToken);
	}
}