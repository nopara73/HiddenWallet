using NBitcoin;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Helpers;

namespace WalletWasabi.Tests.UnitTests.Transactions
{
	public class TransactionStoreMock : TransactionStore
	{
		public TransactionStoreMock([CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
		{
			// Make sure starts with clear state.
			var filePath = Path.Combine(Global.GetWorkDir(callerFilePath, callerMemberName), "Transactions.dat");
			if (File.Exists(filePath))
			{
				File.Delete(filePath);
			}
		}

		public async Task InitializeAsync(Network network, [CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
		{
			var dir = Global.GetWorkDir(callerFilePath, callerMemberName);
			await InitializeAsync(dir, network, $"{nameof(TransactionStoreMock)}.{nameof(TransactionStoreMock.InitializeAsync)}");
		}
	}
}
