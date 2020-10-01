using NBitcoin;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Tor;

namespace WalletWasabi.Tests
{
	public static class Global
	{
		static Global()
		{
			TorSocks5Endpoint = new IPEndPoint(IPAddress.Loopback, 9050);

			DataDir = EnvironmentHelpers.GetDataDir(Path.Combine("WalletWasabi", "Tests"));

			string torLogsFile = Path.Combine(DataDir, "TorLogs.txt");
			string torDistributionFolder = Path.Combine(EnvironmentHelpers.GetFullBaseDirectory(), "TorDaemons");

			TorSettings = new TorSettings(DataDir, torLogsFile, torDistributionFolder);

			Logger.SetFilePath(Path.Combine(DataDir, "Logs.txt"));
			Logger.SetMinimumLevel(LogLevel.Info);
			Logger.SetModes(LogMode.Debug, LogMode.Console, LogMode.File);
		}

		public static string DataDir { get; }

		public static EndPoint TorSocks5Endpoint { get; }

		public static TorSettings TorSettings { get; }

		public static SmartTransaction GenerateRandomSmartTransaction()
		{
			var tx = Transaction.Create(Network.Main);
			tx.Outputs.Add(Money.Coins(1), new Key());
			return new SmartTransaction(tx, Height.Mempool);
		}

		public static string GetWorkDir([CallerFilePath] string callerFilePath = null, [CallerMemberName] string callerMemberName = null)
		{
			return Path.Combine(DataDir, EnvironmentHelpers.ExtractFileName(callerFilePath), callerMemberName);
		}
	}
}
