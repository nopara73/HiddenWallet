using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.AddWallet
{
	public enum WalletType
	{
		Normal,
		Hardware,
		Coldcard,
		Trezor,
		Ledger
	}

	public class AddedWalletPageViewModel : RoutableViewModel
	{
		public AddedWalletPageViewModel(string walletName, WalletType type)
		{
			WalletName = walletName;
			Type = type;

			NextCommand = ReactiveCommand.Create(() =>
			{
				Navigate().Clear();
			});
		}

		public WalletType Type { get; }

		public string WalletName { get; }
	}
}
