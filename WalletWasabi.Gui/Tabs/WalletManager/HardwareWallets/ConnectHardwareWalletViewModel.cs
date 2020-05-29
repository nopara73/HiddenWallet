using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DynamicData;
using DynamicData.Binding;
using NBitcoin;
using Newtonsoft.Json.Linq;
using ReactiveUI;
using Splat;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Gui.Controls.WalletExplorer;
using WalletWasabi.Gui.Helpers;
using WalletWasabi.Gui.Models.StatusBarStatuses;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Helpers;
using WalletWasabi.Hwi;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Logging;

namespace WalletWasabi.Gui.Tabs.WalletManager.HardwareWallets
{
	public class ConnectHardwareWalletViewModel : CategoryViewModel
	{
		private ObservableCollection<HardwareWalletViewModel> _wallets;
		private HardwareWalletViewModel _selectedWallet;
		private bool _isBusy;
		private bool _isHardwareBusy;
		private string _loadButtonText;
		private bool _isHwWalletSearchTextVisible;

		private void ImportHardwareWallet(JObject json, string name, bool shouldReverseMfp)
		{
			var xpubString = json["ExtPubKey"].ToString();
			var mfpString = json["MasterFingerprint"].ToString();
			var bytes = ByteHelpers.FromHex(Guard.NotNullOrEmptyOrWhitespace(nameof(mfpString), mfpString, trim: true));
			HDFingerprint mfp = shouldReverseMfp ? new HDFingerprint(bytes.Reverse().ToArray()) : new HDFingerprint(bytes);

			ExtPubKey extPubKey = NBitcoinHelpers.BetterParseExtPubKey(xpubString);

			Logger.LogInfo("Creating a new wallet file.");
			var walletName = WalletManager.WalletDirectories.GetNextWalletName(name);
			var walletFullPath = WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;
			var keymanager = KeyManager.CreateNewHardwareWalletWatchOnly(mfp, extPubKey, walletFullPath);
			WalletManager.AddWallet(keymanager);
			Owner.SelectLoadWallet(keymanager);
		}

		public ConnectHardwareWalletViewModel(WalletManagerViewModel owner) : base("Hardware Wallet")
		{
			Global = Locator.Current.GetService<Global>();
			WalletManager = Global.WalletManager;
			Owner = owner;
			Wallets = new ObservableCollection<HardwareWalletViewModel>();
			IsHwWalletSearchTextVisible = false;

			this.WhenAnyValue(x => x.SelectedWallet)
				.Where(x => x is null)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ =>
				{
					SelectedWallet = Wallets.FirstOrDefault();
					SetLoadButtonText();
				});

			Wallets
				.ToObservableChangeSet()
				.ToCollection()
				.Where(items => items.Any() && SelectedWallet is null)
				.Select(items => items.First())
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x => SelectedWallet = x);

			this.WhenAnyValue(x => x.IsBusy, x => x.IsHardwareBusy)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => SetLoadButtonText());

			LoadCommand = ReactiveCommand.CreateFromTask(LoadWalletAsync, this.WhenAnyValue(x => x.SelectedWallet, x => x.IsBusy).Select(x => x.Item1 is { } && !x.Item2));
			ImportHardwareWalletCommand = ReactiveCommand.CreateFromTask(async () =>
			{
				var ofd = new OpenFileDialog
				{
					AllowMultiple = false,
					Title = "Import Hardware Wallet"
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
					bool shouldReverseMfp = false;
					var coldCardVersionString = json["ColdCardFirmwareVersion"]?.ToString();
					if (coldCardVersionString is string)
					{
						Version coldCardVersion = new Version(coldCardVersionString);
						// https://github.com/zkSNACKs/WalletWasabi/pull/1663#issuecomment-508073066
						// Coldcard 2.1.0 improperly implemented Wasabi skeleton fingerprint at first, so we must reverse byte order.
						// The solution was to add a ColdCardFirmwareVersion json field from 2.1.1 and correct the one generated by 2.1.0.
						if (coldCardVersion == new Version("2.1.0")) // Should never happen though.
						{
							shouldReverseMfp = true;
						}
						ImportHardwareWallet(json, "Coldcard", shouldReverseMfp);
						return;
					}
					var coboVaultVersionString = json["CoboVaultFirmwareVersion"]?.ToString();
					if (coboVaultVersionString is string)
					{
						ImportHardwareWallet(json, "CoboVault", shouldReverseMfp);
						return;
					}
					ImportHardwareWallet(json, "HardwareWallet", shouldReverseMfp);
				}
			});
			EnumerateHardwareWalletsCommand = ReactiveCommand.CreateFromTask(async () => await EnumerateIfHardwareWalletsAsync());
			OpenBrowserCommand = ReactiveCommand.CreateFromTask<string>(IoHelpers.OpenBrowserAsync);

			Observable
				.Merge(LoadCommand.ThrownExceptions)
				.Merge(OpenBrowserCommand.ThrownExceptions)
				.Merge(ImportHardwareWalletCommand.ThrownExceptions)
				.Merge(EnumerateHardwareWalletsCommand.ThrownExceptions)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(ex =>
				{
					Logger.LogError(ex);
					NotificationHelpers.Error(ex.ToUserFriendlyString());
				});
		}

		public bool IsHwWalletSearchTextVisible
		{
			get => _isHwWalletSearchTextVisible;
			set => this.RaiseAndSetIfChanged(ref _isHwWalletSearchTextVisible, value);
		}

		public ObservableCollection<HardwareWalletViewModel> Wallets
		{
			get => _wallets;
			set => this.RaiseAndSetIfChanged(ref _wallets, value);
		}

		public HardwareWalletViewModel SelectedWallet
		{
			get => _selectedWallet;
			set => this.RaiseAndSetIfChanged(ref _selectedWallet, value);
		}

		public string LoadButtonText
		{
			get => _loadButtonText;
			set => this.RaiseAndSetIfChanged(ref _loadButtonText, value);
		}

		public bool IsBusy
		{
			get => _isBusy;
			set => this.RaiseAndSetIfChanged(ref _isBusy, value);
		}

		public bool IsHardwareBusy
		{
			get => _isHardwareBusy;
			set => this.RaiseAndSetIfChanged(ref _isHardwareBusy, value);
		}

		public ReactiveCommand<Unit, Unit> LoadCommand { get; }
		public ReactiveCommand<Unit, Unit> ImportHardwareWalletCommand { get; set; }
		public ReactiveCommand<Unit, Unit> EnumerateHardwareWalletsCommand { get; set; }
		public ReactiveCommand<string, Unit> OpenBrowserCommand { get; }
		public string UDevRulesLink => "https://github.com/bitcoin-core/HWI/tree/master/hwilib/udev";
		public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
		private Global Global { get; }
		private Wallets.WalletManager WalletManager { get; }
		private WalletManagerViewModel Owner { get; }

		public void SetLoadButtonText()
		{
			var text = "Load Wallet";
			if (IsHardwareBusy)
			{
				text = "Waiting for Hardware Wallet...";
			}
			else if (IsBusy)
			{
				text = "Loading...";
			}
			else
			{
				// If the hardware wallet was not initialized, then make the button say Setup, not Load.
				// If pin is needed, then make the button say Send Pin instead.

				if (SelectedWallet?.HardwareWalletInfo is { })
				{
					if (!SelectedWallet.HardwareWalletInfo.IsInitialized())
					{
						text = "Setup Wallet";
					}

					if (SelectedWallet.HardwareWalletInfo.NeedsPinSent is true)
					{
						text = "Send PIN";
					}
				}
			}

			LoadButtonText = text;
		}

		public async Task<KeyManager> LoadKeyManagerAsync()
		{
			try
			{
				var selectedWallet = SelectedWallet;
				if (selectedWallet is null)
				{
					NotificationHelpers.Warning("No wallet selected.");
					return null;
				}

				var walletName = selectedWallet.WalletName;

				var client = new HwiClient(Global.Network);

				if (selectedWallet.HardwareWalletInfo is null)
				{
					NotificationHelpers.Warning("No hardware wallet detected.");
					return null;
				}

				if (!selectedWallet.HardwareWalletInfo.IsInitialized())
				{
					try
					{
						IsHardwareBusy = true;
						MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType.SettingUpHardwareWallet);

						// Setup may take a while for users to write down stuff.
						using var ctsSetup = new CancellationTokenSource(TimeSpan.FromMinutes(21));

						// Trezor T doesn't require interactive mode.
						if (selectedWallet.HardwareWalletInfo.Model == HardwareWalletModels.Trezor_T
							|| selectedWallet.HardwareWalletInfo.Model == HardwareWalletModels.Trezor_T_Simulator)
						{
							await client.SetupAsync(selectedWallet.HardwareWalletInfo.Model, selectedWallet.HardwareWalletInfo.Path, false, ctsSetup.Token);
						}
						else
						{
							await client.SetupAsync(selectedWallet.HardwareWalletInfo.Model, selectedWallet.HardwareWalletInfo.Path, true, ctsSetup.Token);
						}

						MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType.ConnectingToHardwareWallet);
						await EnumerateIfHardwareWalletsAsync();
					}
					finally
					{
						IsHardwareBusy = false;
						MainWindowViewModel.Instance.StatusBar.TryRemoveStatus(StatusType.SettingUpHardwareWallet, StatusType.ConnectingToHardwareWallet);
					}

					return await LoadKeyManagerAsync();
				}
				else if (selectedWallet.HardwareWalletInfo.NeedsPinSent is true)
				{
					await PinPadViewModel.UnlockAsync(selectedWallet.HardwareWalletInfo);

					var p = selectedWallet.HardwareWalletInfo.Path;
					var t = selectedWallet.HardwareWalletInfo.Model;
					await EnumerateIfHardwareWalletsAsync();
					selectedWallet = Wallets.FirstOrDefault(x => x.HardwareWalletInfo.Model == t && x.HardwareWalletInfo.Path == p);
					if (selectedWallet is null)
					{
						NotificationHelpers.Warning("Could not find the hardware wallet. Did you disconnect it?");
						return null;
					}
					else
					{
						SelectedWallet = selectedWallet;
					}

					if (!selectedWallet.HardwareWalletInfo.IsInitialized())
					{
						NotificationHelpers.Warning("Hardware wallet is not initialized.");
						return null;
					}

					if (selectedWallet.HardwareWalletInfo.NeedsPinSent is true)
					{
						NotificationHelpers.Warning("Hardware wallet needs a PIN to be sent.");
						return null;
					}
				}

				ExtPubKey extPubKey;
				using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
				try
				{
					MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType.AcquiringXpubFromHardwareWallet);
					extPubKey = await client.GetXpubAsync(selectedWallet.HardwareWalletInfo.Model, selectedWallet.HardwareWalletInfo.Path, KeyManager.DefaultAccountKeyPath, cts.Token);
				}
				finally
				{
					MainWindowViewModel.Instance.StatusBar.TryRemoveStatus(StatusType.AcquiringXpubFromHardwareWallet);
				}

				if (TryFindWalletByExtPubKey(extPubKey, out string wn))
				{
					walletName = wn.TrimEnd(".json", StringComparison.OrdinalIgnoreCase);
				}
				else
				{
					Logger.LogInfo("Hardware wallet was not used previously on this computer. Creating a new wallet file.");

					var prefix = selectedWallet.HardwareWalletInfo is null ? "HardwareWallet" : selectedWallet.HardwareWalletInfo.Model.ToString();

					walletName = WalletManager.WalletDirectories.GetNextWalletName(prefix);
					var path = WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;

					// Get xpub should had triggered passphrase request, so the fingerprint should be available here.
					if (!selectedWallet.HardwareWalletInfo.Fingerprint.HasValue)
					{
						await EnumerateIfHardwareWalletsAsync();
						selectedWallet = Wallets.FirstOrDefault(x => x.HardwareWalletInfo.Model == selectedWallet.HardwareWalletInfo.Model && x.HardwareWalletInfo.Path == selectedWallet.HardwareWalletInfo.Path);
					}
					if (!selectedWallet.HardwareWalletInfo.Fingerprint.HasValue)
					{
						throw new InvalidOperationException("Hardware wallet did not provide fingerprint.");
					}
					WalletManager.AddWallet(KeyManager.CreateNewHardwareWalletWatchOnly(selectedWallet.HardwareWalletInfo.Fingerprint.Value, extPubKey, path));
				}

				KeyManager keyManager = WalletManager.GetWalletByName(walletName).KeyManager;

				return keyManager;
			}
			catch (Exception ex)
			{
				try
				{
					await EnumerateIfHardwareWalletsAsync();
				}
				catch (Exception ex2)
				{
					Logger.LogError(ex2);
				}

				// Initialization failed.
				Logger.LogError(ex);
				NotificationHelpers.Error(ex.ToUserFriendlyString());

				return null;
			}
			finally
			{
				SetLoadButtonText();
			}
		}

		public async Task LoadWalletAsync()
		{
			try
			{
				IsBusy = true;

				var keyManager = await LoadKeyManagerAsync();
				if (keyManager is null)
				{
					return;
				}

				try
				{
					var wallet = await Task.Run(async () => await WalletManager.StartWalletAsync(keyManager));
					// Successfully initialized.
					Owner.OnClose();
				}
				catch (OperationCanceledException ex)
				{
					Logger.LogTrace(ex);
				}
				catch (Exception ex)
				{
					Logger.LogError(ex);
					NotificationHelpers.Error(ex.ToUserFriendlyString());
				}
			}
			finally
			{
				IsBusy = false;
			}
		}

		protected async Task EnumerateIfHardwareWalletsAsync()
		{
			using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
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
			}
			finally
			{
				IsHwWalletSearchTextVisible = false;
			}
		}

		private bool TryFindWalletByExtPubKey(ExtPubKey extPubKey, out string walletName)
		{
			walletName = WalletManager.WalletDirectories
				.EnumerateWalletFiles(includeBackupDir: false)
				.FirstOrDefault(fi => KeyManager.TryGetExtPubKeyFromFile(fi.FullName, out ExtPubKey epk) && epk == extPubKey)
				?.Name;

			return walletName is { };
		}
	}
}
