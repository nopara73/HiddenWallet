using NBitcoin;
using NBitcoin.Protocol;
using System;

namespace WalletWasabi.Helpers
{
	public static class Constants
	{
		public const string ClientSupportBackendVersionMin = "4";
		public const string ClientSupportBackendVersionMax = "4";
		public const string BackendMajorVersion = "4";

		/// <summary>
		/// By changing this, we can force to start over the transactions file, so old incorrect transactions would be cleared.
		/// It is also important to force the KeyManagers to be reindexed when this is changed by renaming the BlockState Height related property.
		/// </summary>
		public const string ConfirmedTransactionsVersion = "2";

		public const uint ProtocolVersionWitnessVersion = 70012;

		public const int P2wpkhInputSizeInBytes = 41;
		public const int P2wpkhInputVirtualSize = 69;
		public const int P2pkhInputSizeInBytes = 145;
		public const int OutputSizeInBytes = 33;

		// https://en.bitcoin.it/wiki/Bitcoin
		// There are a maximum of 2,099,999,997,690,000 Bitcoin elements (called satoshis), which are currently most commonly measured in units of 100,000,000 known as BTC. Stated another way, no more than 21 million BTC can ever be created.
		public const long MaximumNumberOfSatoshis = 2099999997690000;

		public const decimal MaximumNumberOfBitcoins = 20999999.9769m;

		public const int FastestConfirmationTarget = 1;
		public const int TwentyMinutesConfirmationTarget = 2;
		public const int OneDayConfirmationTarget = 144;
		public const int SevenDaysConfirmationTarget = 1008;

		public const int BigFileReadWriteBufferSize = 1 * 1024 * 1024;

		public const int DefaultMainNetBitcoinP2pPort = 8333;
		public const int DefaultTestNetBitcoinP2pPort = 18333;
		public const int DefaultRegTestBitcoinP2pPort = 18444;

		public const int DefaultMainNetBitcoinCoreRpcPort = 8332;
		public const int DefaultTestNetBitcoinCoreRpcPort = 18332;
		public const int DefaultRegTestBitcoinCoreRpcPort = 18443;

		public const decimal DefaultDustThreshold = 0.00005m;

		public const string AlphaNumericCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
		public const string CapitalAlphaNumericCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

		public const string ExecutableName = "wassabee";
		public const string AppName = "Wasabi Wallet";
		public const string BuiltinBitcoinNodeName = "Bitcoin Knots";

		public static readonly Version ClientVersion = new(1, 1, 12, 9);
		public static readonly Version HwiVersion = new("2.0.2");
		public static readonly Version BitcoinCoreVersion = new("0.21.1");
		public static readonly Version LegalDocumentsVersion = new(2, 0);

		public static readonly NodeRequirement NodeRequirements = new()
		{
			RequiredServices = NodeServices.NODE_WITNESS,
			MinVersion = ProtocolVersionWitnessVersion,
			MinProtocolCapabilities = new ProtocolCapabilities { SupportGetBlock = true, SupportWitness = true, SupportMempoolQuery = true }
		};

		public static readonly NodeRequirement LocalNodeRequirements = new()
		{
			RequiredServices = NodeServices.NODE_WITNESS,
			MinVersion = ProtocolVersionWitnessVersion,
			MinProtocolCapabilities = new ProtocolCapabilities { SupportGetBlock = true, SupportWitness = true }
		};

		public static readonly ExtPubKey FallBackCoordinatorExtPubKey = NBitcoinHelpers.BetterParseExtPubKey("xpub6D2PqhWBAbF3xgfaAUW73KnaCXUroArcgMTzNkNzfVX7ykkSzQGbqaXZeaNyxKbZojAAqDwsne6B7NcVhiTrXbGYrQNq1yF76NkgdonGrEa");

		public static readonly string[] UserAgents = new[]
		{
			"/Satoshi:0.21.0/",
			"/Satoshi:0.20.1/",
			"/Satoshi:0.20.0/",
			"/Satoshi:0.19.1/",
			"/Satoshi:0.19.0.1/",
			"/Satoshi:0.19.0/",
			"/Satoshi:0.18.1/",
			"/Satoshi:0.18.0/",
			"/Satoshi:0.17.1/",
			"/Satoshi:0.17.0.1/",
			"/Satoshi:0.17.0/",
			"/Satoshi:0.16.3/",
			"/Satoshi:0.16.2/",
			"/Satoshi:0.16.1/",
			"/Satoshi:0.16.0/",
		};

		public static readonly int[] ConfirmationTargets = new[]
		{
			2, // Twenty Minutes
			3, // Thirty Minutes
			6, // One Hour
			18, // Three Hours
			36, // Six Hours
			72, // Twelve Hours
			144, // One Day
			432, // Three Days
			1008, // Seven Days
		};

		public static string ClientSupportBackendVersionText => ClientSupportBackendVersionMin == ClientSupportBackendVersionMax
				? ClientSupportBackendVersionMin
				: $"{ClientSupportBackendVersionMin} - {ClientSupportBackendVersionMax}";
	}
}
