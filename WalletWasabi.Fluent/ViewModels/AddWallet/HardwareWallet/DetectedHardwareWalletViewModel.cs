using System;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Extensions;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.ViewModels.AddWallet.HardwareWallet
{
	public class DetectedHardwareWalletViewModel : RoutableViewModel
	{
		public DetectedHardwareWalletViewModel(HardwareWalletOperations hardwareWalletOperations, string walletName)
		{
			WalletName = walletName;

			switch (hardwareWalletOperations.SelectedDevice!.Model)
			{
				case HardwareWalletModels.Coldcard:
				case HardwareWalletModels.Coldcard_Simulator:
					Type = WalletType.Coldcard;
					break;

				case HardwareWalletModels.Ledger_Nano_S:
					Type = WalletType.Ledger;
					break;

				case HardwareWalletModels.Trezor_1:
				case HardwareWalletModels.Trezor_1_Simulator:
				case HardwareWalletModels.Trezor_T:
				case HardwareWalletModels.Trezor_T_Simulator:
					Type = WalletType.Trezor;
					break;
				default:
					Type = WalletType.Hardware;
					break;
			}

			TypeName = hardwareWalletOperations.SelectedDevice.Model.FriendlyName();

			NextCommand = ReactiveCommand.CreateFromTask(async () =>
			{
				IsBusy = true;

				try
				{
					await hardwareWalletOperations.GenerateWalletAsync(WalletName);
					hardwareWalletOperations.Dispose();
					Navigate().To(new AddedWalletPageViewModel(WalletName, Type));
				}
				catch(Exception ex)
				{
					// TODO: Notify the user
					Logger.LogError(ex);
					Navigate().Back();
				}

				IsBusy = false;
			});

			NoCommand = ReactiveCommand.Create(() =>
			{
				Navigate().Back();
			});
		}

		public string WalletName { get; }

		public WalletType Type { get; }

		public string TypeName { get; }

		public ICommand NoCommand { get; }
	}
}
