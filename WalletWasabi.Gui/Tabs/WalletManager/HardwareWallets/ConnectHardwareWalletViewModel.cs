using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using AvalonStudio.Extensibility;
using NBitcoin;
using Newtonsoft.Json.Linq;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Gui.Controls.WalletExplorer;
using WalletWasabi.Gui.Helpers;
using WalletWasabi.Gui.Tabs.WalletManager.LoadWallets;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Helpers;
using WalletWasabi.Hwi;
using WalletWasabi.Logging;

namespace WalletWasabi.Gui.Tabs.WalletManager.HardwareWallets
{
	public class ConnectHardwareWalletViewModel : CategoryViewModel
	{
		private bool _isHwWalletSearchTextVisible;
		private HardwareWalletViewModel _selectedWallet;

		public ConnectHardwareWalletViewModel(WalletManagerViewModel owner) : base("Hardware Wallet")
		{
			Global = Locator.Current.GetService<Global>();
			Owner = owner;
			Wallets = new ObservableCollection<HardwareWalletViewModel>();

			ImportColdcardCommand = ReactiveCommand.CreateFromTask(async () =>
			{
				var ofd = new OpenFileDialog
				{
					AllowMultiple = false,
					Title = "Import Coldcard"
				};

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				{
					ofd.Directory = Path.Combine("/media", Environment.UserName);
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					ofd.Directory = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
				}

				var window = (Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime).MainWindow;
				var selected = await ofd.ShowAsync(window, fallBack: true);
				if (selected is { } && selected.Any())
				{
					var path = selected.First();
					var jsonString = await File.ReadAllTextAsync(path);
					var json = JObject.Parse(jsonString);
					var xpubString = json["ExtPubKey"].ToString();
					var mfpString = json["MasterFingerprint"].ToString();

					// https://github.com/zkSNACKs/WalletWasabi/pull/1663#issuecomment-508073066
					// Coldcard 2.1.0 improperly implemented Wasabi skeleton fingerprint at first, so we must reverse byte order.
					// The solution was to add a ColdCardFirmwareVersion json field from 2.1.1 and correct the one generated by 2.1.0.
					var coldCardVersionString = json["ColdCardFirmwareVersion"]?.ToString();
					var reverseByteOrder = false;
					if (coldCardVersionString is null)
					{
						reverseByteOrder = true;
					}
					else
					{
						Version coldCardVersion = new Version(coldCardVersionString);

						if (coldCardVersion == new Version("2.1.0")) // Should never happen though.
						{
							reverseByteOrder = true;
						}
					}

					var bytes = ByteHelpers.FromHex(Guard.NotNullOrEmptyOrWhitespace(nameof(mfpString), mfpString, trim: true));
					HDFingerprint mfp = reverseByteOrder ? new HDFingerprint(bytes.Reverse().ToArray()) : new HDFingerprint(bytes);

					ExtPubKey extPubKey = NBitcoinHelpers.BetterParseExtPubKey(xpubString);

					Logger.LogInfo("Creating a new wallet file.");
					var walletName = Global.WalletManager.WalletDirectories.GetNextWalletName("Coldcard");
					var walletFullPath = Global.WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;
					KeyManager.CreateNewHardwareWalletWatchOnly(mfp, extPubKey, walletFullPath);
					owner.SelectLoadWallet();
				}
			});

			EnumerateHardwareWalletsCommand = ReactiveCommand.CreateFromTask(async () => await EnumerateIfHardwareWalletsAsync());
			OpenBrowserCommand = ReactiveCommand.CreateFromTask<string>(IoHelpers.OpenBrowserAsync);

			Observable
				.Merge(OpenBrowserCommand.ThrownExceptions)
				.Merge(ImportColdcardCommand.ThrownExceptions)
				.Merge(EnumerateHardwareWalletsCommand.ThrownExceptions)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(ex =>
				{
					Logger.LogError(ex);
					NotificationHelpers.Error(ex.ToUserFriendlyString());
				});
		}

		public ReactiveCommand<Unit, Unit> ImportColdcardCommand { get; set; }
		public ReactiveCommand<Unit, Unit> EnumerateHardwareWalletsCommand { get; set; }
		public ReactiveCommand<string, Unit> OpenBrowserCommand { get; }
		public string UDevRulesLink => "https://github.com/bitcoin-core/HWI/tree/master/hwilib/udev";
		public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

		public bool IsHwWalletSearchTextVisible
		{
			get => _isHwWalletSearchTextVisible;
			set => this.RaiseAndSetIfChanged(ref _isHwWalletSearchTextVisible, value);
		}

		public HardwareWalletViewModel SelectedWallet
		{
			get => _selectedWallet;
			set => this.RaiseAndSetIfChanged(ref _selectedWallet, value);
		}

		private Global Global { get; }
		private WalletManagerViewModel Owner { get; }
		public ObservableCollection<HardwareWalletViewModel> Wallets { get; }

		protected async Task EnumerateIfHardwareWalletsAsync()
		{
			var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
			IsHwWalletSearchTextVisible = true;
			try
			{
				var client = new HwiClient(Global.Network);
				var devices = await client.EnumerateAsync(cts.Token);

				Wallets.Clear();
				foreach (var dev in devices)
				{
					var walletEntry = new HardwareWalletViewModel(dev);
					Wallets.Add(walletEntry);
				}
				TrySetWalletStates();
			}
			finally
			{
				IsHwWalletSearchTextVisible = false;
				cts.Dispose();
			}
		}
	}
}
