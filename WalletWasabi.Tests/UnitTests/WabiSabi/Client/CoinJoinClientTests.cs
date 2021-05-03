using Moq;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WalletWasabi.BitcoinCore.Rpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi.Backend;
using WalletWasabi.WabiSabi.Backend.Banning;
using WalletWasabi.WabiSabi.Backend.PostRequests;
using WalletWasabi.WabiSabi.Backend.Rounds;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.WabiSabi.Crypto;
using WalletWasabi.Wallets;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Client
{
	public class CoinJoinClientTests
	{
		[Fact]
		public async Task CoinJoinClientTestAsync()
		{
			var config = new WabiSabiConfig { MaxInputCountByRound = 1 };
			var round = WabiSabiFactory.CreateRound(config);
			using Arena arena = await WabiSabiFactory.CreateAndStartArenaAsync(config, round);
			await arena.TriggerAndWaitRoundAsync(TimeSpan.FromMinutes(1));

			ClientRound clientRound = new(round);

			using var key = new Key();
			var outpoint = BitcoinFactory.CreateOutPoint();
			var mockRpc = new Mock<IRPCClient>();
			mockRpc.Setup(rpc => rpc.GetTxOutAsync(outpoint.Hash, (int)outpoint.N, true))
				.ReturnsAsync(new NBitcoin.RPC.GetTxOutResponse
				{
					IsCoinBase = false,
					Confirmations = 200,
					TxOut = new TxOut(Money.Coins(1m), key.PubKey.WitHash.GetAddress(Network.Main)),
				});
			await using var coordinator = new ArenaRequestHandler(config, new Prison(), arena, mockRpc.Object);

			CredentialPool amountCredentials = new();
			CredentialPool vsizeCredentials = new();

			string password = "whiterabbit";
			var km = ServiceFactory.CreateKeyManager(password);
			var smartCoin = BitcoinFactory.CreateSmartCoin(BitcoinFactory.CreateHdPubKey(km), Money.Coins(1));
			Kitchen kitchen = new();
			kitchen.Cook(password);

			using CoinJoinClient coinJoinClient = new(clientRound, coordinator, new[] { smartCoin.Coin }, kitchen, km);
			await coinJoinClient.StartMixingCoinsAsync();
		}
	}
}