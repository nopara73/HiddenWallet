using System;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Bases;
using WalletWasabi.Helpers;
using WalletWasabi.Wallets;

namespace WalletWasabi.Services
{
	public class SystemAwakeChecker : PeriodicRunner
	{
		public SystemAwakeChecker(WalletManager walletManager, SynchronizationContext? synchronizationContext) : base(TimeSpan.FromMinutes(1))
		{
			WalletManager = walletManager;
			SynchronizationContext = synchronizationContext;
		}

		private WalletManager WalletManager { get; }
		public SynchronizationContext? SynchronizationContext { get; }

		protected override async Task ActionAsync(CancellationToken cancel)
		{
			if (WalletManager.AnyCoinJoinInProgress())
			{
				await EnvironmentHelpers.ProlongSystemAwakeAsync(SynchronizationContext).ConfigureAwait(false);
			}
		}
	}
}
