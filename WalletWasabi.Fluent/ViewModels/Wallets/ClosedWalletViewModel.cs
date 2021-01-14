using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Wallets.HardwareWallet;
using WalletWasabi.Fluent.ViewModels.Wallets.WatchOnlyWallet;
using WalletWasabi.Gui.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets
{
	public partial class ClosedWalletViewModel : WalletViewModelBase
	{
		[AutoNotify] private ObservableCollection<NavBarItemViewModel> _items;

		protected ClosedWalletViewModel(WalletManager walletManager, Wallet wallet, LegalChecker legalChecker) : base(wallet, legalChecker)
		{
			_items = new ObservableCollection<NavBarItemViewModel>();

			OpenWalletCommand = ReactiveCommand.CreateFromTask(
				async () =>
				{
					try
					{
						if (wallet.KeyManager.PasswordVerified is true)
						{
							// TODO ... new UX will test password earlier...
						}

						await Task.Run(async () => await walletManager.StartWalletAsync(Wallet));
					}
					catch (OperationCanceledException ex)
					{
						Logger.LogTrace(ex);
					}
					catch (Exception ex)
					{
						NotificationHelpers.Error($"Couldn't load wallet. Reason: {ex.ToUserFriendlyString()}", sender: wallet);
						Logger.LogError(ex);
					}
				},
				this.WhenAnyValue(x => x.WalletState).Select(x => x == WalletState.Uninitialized));
		}

		public ReactiveCommand<Unit, Unit> OpenWalletCommand { get; }

		public override string IconName => "web_asset_regular";

		public static WalletViewModelBase Create(WalletManager walletManager, Wallet wallet, LegalChecker legalChecker)
		{
			return wallet.KeyManager.IsHardwareWallet
				? new ClosedHardwareWalletViewModel(walletManager, wallet, legalChecker)
				: wallet.KeyManager.IsWatchOnly
					? new ClosedWatchOnlyWalletViewModel(walletManager, wallet, legalChecker)
					: new ClosedWalletViewModel(walletManager, wallet, legalChecker);
		}
	}
}
