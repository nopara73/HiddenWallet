using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using System.Timers;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.AddWallet.HardwareWallet
{
	public partial class ConnectHardwareWalletViewModel : RoutableViewModel
	{
		[AutoNotify] private string _message;
		[AutoNotify] private bool _continueButtonEnable;

		public ConnectHardwareWalletViewModel(string walletName, Network network, WalletManager walletManager)
		{
			IsBusy = true;

			WalletName = walletName;
			HardwareWalletOperations = new HardwareWalletOperations(walletManager, network);
			HardwareWalletOperations.NoHardwareWalletFound += OnNoHardwareWalletFound;

			BackCommand = ReactiveCommand.Create(() =>
			{
				HardwareWalletOperations.Dispose();
				Navigate().Back();
			});

			CancelCommand = ReactiveCommand.Create(() =>
			{
				HardwareWalletOperations.Dispose();
				Navigate().Clear();
			});

			NextCommand = ReactiveCommand.Create(() =>
			{
				HardwareWalletOperations.StartDetection();
				ContinueButtonEnable = false;
				Message = "";
			});

			_message = "";
		}

		public string WalletName { get; }

		public HardwareWalletOperations HardwareWalletOperations { get; }

		private void OnNoHardwareWalletFound(object? sender, EventArgs e)
		{
			IsBusy = false;
			ContinueButtonEnable = true;
			Message = "Make sure your device is unlocked with PIN and plugged in then press Continue.";
			Task.Run(() => HardwareWalletOperations.StopDetectionAsync());
			HardwareWalletOperations.NoHardwareWalletFound -= OnNoHardwareWalletFound;
		}

		private void OnPassphraseNeeded(object sender, ElapsedEventArgs e)
		{
			IsBusy = false;
			Message = "Check your device and enter your passphrase.";
		}

		private void OnHardwareWalletsFound(object? sender, HwiEnumerateEntry[] devices)
		{
			IsBusy = false;

			if (devices.Length > 1)
			{
				Message = "Make sure you have only one hardware wallet connected to the PC.";
				return;
			}

			var device = devices[0];

			if (device.Code is { })
			{
				Message = "Something happened with your device, unlock it with your PIN/Passphrase or reconnect to the PC.";
				return;
			}

			if (device.NeedsPassphraseSent == true)
			{
				Message = "Enter your passphrase on your device.";
				return;
			}

			if (device.NeedsPinSent == true)
			{
				Message = "Enter your PIN on your device.";
				return;
			}

			if (!device.IsInitialized())
			{
				if (device.Model == HardwareWalletModels.Coldcard)
				{
					Message = "Initialize your device first.";
				}
				else
				{
					Message = "Check your device and finish the initialization.";
					Task.Run(() => HardwareWalletOperations.InitHardwareWalletAsync(device));
				}

				return;
			}

			HardwareWalletOperations.SelectedDevice = device;
			Navigate().To(new DetectedHardwareWalletViewModel(HardwareWalletOperations, WalletName));
		}

		protected override void OnNavigatedTo(bool inStack, CompositeDisposable disposable)
		{
			base.OnNavigatedTo(inStack, disposable);

			Message = "";

			HardwareWalletOperations.HardwareWalletsFound += OnHardwareWalletsFound;
			HardwareWalletOperations.PassphraseTimer.Elapsed += OnPassphraseNeeded;

			Disposable.Create(() =>
			{
				HardwareWalletOperations.HardwareWalletsFound -= OnHardwareWalletsFound;
				HardwareWalletOperations.NoHardwareWalletFound -= OnNoHardwareWalletFound;
				HardwareWalletOperations.PassphraseTimer.Elapsed -= OnPassphraseNeeded;
			})
			.DisposeWith(disposable);
		}
	}
}
