using ReactiveUI;
using System;
using System.IO;
using System.Reactive.Disposables;
using System.Windows.Input;
using System.Reactive.Linq;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Wallets;
using WalletWasabi.Stores;
using NBitcoin;
using WalletWasabi.Fluent.ViewModels.Dialogs;
using System.Threading.Tasks;
using WalletWasabi.Fluent.ViewModels.AddWallet.Create;
using WalletWasabi.Fluent.ViewModels.AddWallet.HardwareWallet;
using WalletWasabi.Gui.Validation;
using WalletWasabi.Models;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Legal;

namespace WalletWasabi.Fluent.ViewModels.AddWallet
{
	[NavigationMetaData(
		Title = "Add Wallet",
		Caption = "Create, recover or import wallet",
		Order = 2,
		Category = "General",
		Keywords = new[] { "Wallet", "Add", "Create", "Recover", "Import", "Connect", "Hardware", "ColdCard", "Trezor", "Ledger" },
		IconName = "add_circle_regular",
		NavBarPosition = NavBarPosition.Bottom,
		NavigationTarget = NavigationTarget.DialogScreen)]
	public partial class AddWalletPageViewModel : NavBarItemViewModel
	{
		[AutoNotify] private string _walletName = "";
		[AutoNotify] private bool _optionsEnabled;

		private readonly LegalDocuments _legalDocuments;

		public AddWalletPageViewModel(
			LegalDocuments legalDocuments,
			WalletManager walletManager,
			BitcoinStore store,
			Network network)
		{
			Title = "Add Wallet";
			SelectionMode = NavBarItemSelectionMode.Button;
			_legalDocuments = legalDocuments;

			this.WhenAnyValue(x => x.WalletName)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Select(x => !string.IsNullOrWhiteSpace(x))
				.Subscribe(x => OptionsEnabled = x && !Validations.Any);

			RecoverWalletCommand = ReactiveCommand.Create(
				() => { Navigate().To(new RecoverWalletViewModel(WalletName, network, walletManager)); });

			ImportWalletCommand = ReactiveCommand.Create(() => new ImportWalletViewModel(WalletName, walletManager));

			ConnectHardwareWalletCommand = ReactiveCommand.Create(() =>
			{
				Navigate().To(new ConnectHardwareWalletViewModel(WalletName, network, walletManager));
			});

			CreateWalletCommand = ReactiveCommand.CreateFromTask(
				async () =>
				{
					var result = await NavigateDialog(
						new EnterPasswordViewModel("Type the password of the wallet and click Continue."));

					if (result is { } password)
					{
						IsBusy = true;

						var (km, mnemonic) = await Task.Run(
							() =>
							{
								var walletGenerator = new WalletGenerator(
									walletManager.WalletDirectories.WalletsDir,
									network)
								{
									TipHeight = store.SmartHeaderChain.TipHeight
								};
								return walletGenerator.GenerateWallet(WalletName, password);
							});

						Navigate().To(new RecoveryWordsViewModel(km, mnemonic, walletManager), NavigationMode.Clear);

						IsBusy = false;
					}
				});

			this.ValidateProperty(x => x.WalletName, errors => ValidateWalletName(errors, walletManager, WalletName));
		}

		protected override void OnNavigatedTo(bool inStack, CompositeDisposable disposable)
		{
			base.OnNavigatedTo(inStack, disposable);

			this.RaisePropertyChanged(WalletName);

			if (!inStack)
			{
				WalletName = "";

				var termsAndConditions = new TermsAndConditionsViewModel(_legalDocuments, this);

				Navigate().To(termsAndConditions);
			}
		}

		private void ValidateWalletName(IValidationErrors errors, WalletManager walletManager, string walletName)
		{
			string walletFilePath = Path.Combine(walletManager.WalletDirectories.WalletsDir, $"{walletName}.json");

			if (string.IsNullOrEmpty(walletName))
			{
				return;
			}

			if (walletName.IsTrimmable())
			{
				errors.Add(ErrorSeverity.Error, "Leading and trailing white spaces are not allowed!");
				return;
			}

			if (File.Exists(walletFilePath))
			{
				errors.Add(
					ErrorSeverity.Error,
					$"A wallet named {walletName} already exists. Please try a different name.");
				return;
			}

			if (!WalletGenerator.ValidateWalletName(walletName))
			{
				errors.Add(ErrorSeverity.Error, "Selected Wallet is not valid. Please try a different name.");
			}
		}

		public ICommand CreateWalletCommand { get; }
		public ICommand RecoverWalletCommand { get; }
		public ICommand ImportWalletCommand { get; }
		public ICommand ConnectHardwareWalletCommand { get; }
	}
}