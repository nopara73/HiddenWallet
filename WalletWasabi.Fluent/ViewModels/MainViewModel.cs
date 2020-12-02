using System;
using System.IO;
using System.Reactive.Concurrency;
using NBitcoin;
using ReactiveUI;
using System.Reactive.Linq;
using WalletWasabi.Fluent.ViewModels.AddWallet;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Fluent.ViewModels.Dialogs;
using Global = WalletWasabi.Gui.Global;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.Search;
using WalletWasabi.Fluent.ViewModels.Settings;

namespace WalletWasabi.Fluent.ViewModels
{
	public partial class MainViewModel : ViewModelBase, IDialogHost
	{
		private readonly Global _global;
		[AutoNotify] private bool _isMainContentEnabled;
		[AutoNotify] private bool _isDialogScreenEnabled;
		[AutoNotify] private DialogViewModelBase? _currentDialog;
		[AutoNotify] private DialogScreenViewModel _dialogScreen;
		[AutoNotify] private NavBarViewModel _navBar;
		[AutoNotify] private StatusBarViewModel _statusBar;
		[AutoNotify] private string _title = "Wasabi Wallet";
		private readonly HomePageViewModel _homePage;
		private readonly SettingsPageViewModel _settingsPage;
		private readonly SearchPageViewModel _searchPage;
		private readonly PrivacyModeViewModel _privacyMode;
		private readonly AddWalletPageViewModel _addWalletPage;

		public MainViewModel(Global global)
		{
			_global = global;

			_dialogScreen = new DialogScreenViewModel();

			MainScreen = new TargettedNavigationStack(NavigationTarget.HomeScreen);

			NavigationState.Register(MainScreen, DialogScreen, () => this);

			Network = global.Network;

			_currentDialog = null;

			_isMainContentEnabled = true;
			_isDialogScreenEnabled = true;

			_statusBar = new StatusBarViewModel(
				global.DataDir,
				global.Network,
				global.Config,
				global.HostedServices,
				global.BitcoinStore.SmartHeaderChain,
				global.Synchronizer,
				global.LegalDocuments);

			var walletManager = new WalletManagerViewModel(global.WalletManager, global.UiConfig);

			_addWalletPage = new AddWalletPageViewModel(
				global.LegalDocuments,
				global.WalletManager,
				global.BitcoinStore,
				global.Network);

			_settingsPage = new SettingsPageViewModel(global.Config, global.UiConfig);
			_privacyMode = new PrivacyModeViewModel(global.UiConfig);
			_homePage = new HomePageViewModel(walletManager, _addWalletPage);
			_searchPage = new SearchPageViewModel();

			_navBar = new NavBarViewModel(MainScreen, walletManager);

			RegisterCategories(_searchPage);
			RegisterViewModels();

			RxApp.MainThreadScheduler.Schedule(async () => await _navBar.InitialiseAsync());

			_searchPage.Initialise();

			MainScreen.To(_homePage);

			this.WhenAnyValue(x => x.DialogScreen!.IsDialogOpen)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x => IsMainContentEnabled = !x);

			this.WhenAnyValue(x => x.CurrentDialog!.IsDialogOpen)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x => IsDialogScreenEnabled = !x);

			walletManager.WhenAnyValue(x => x.Items.Count)
				.Subscribe(x => _navBar.IsHidden = x == 0);
		}

		public TargettedNavigationStack MainScreen { get; }

		public static MainViewModel? Instance { get; internal set; }

		private Network Network { get; }

		public void Initialize()
		{
			// Temporary to keep things running without VM modifications.
			MainWindowViewModel.Instance = new MainWindowViewModel(
				_global.Network,
				_global.UiConfig,
				_global.WalletManager,
				null!,
				null!,
				false);

			StatusBar.Initialize(_global.Nodes.ConnectedNodes);

			if (Network != Network.Main)
			{
				Title += $" - {Network}";
			}
		}

		private void RegisterViewModels()
		{
			HomePageViewModel.Register(_homePage);

			SearchPageViewModel.Register(_searchPage);
			PrivacyModeViewModel.Register(_privacyMode);
			AddWalletPageViewModel.Register(_addWalletPage);
			SettingsPageViewModel.Register(_settingsPage);

			GeneralSettingsTabViewModel.RegisterLazy(
				() =>
				{
					_settingsPage.SelectedTab = 0;
					return _settingsPage;
				});

			PrivacySettingsTabViewModel.RegisterLazy(
				() =>
				{
					_settingsPage.SelectedTab = 1;
					return _settingsPage;
				});

			NetworkSettingsTabViewModel.RegisterLazy(
				() =>
				{
					_settingsPage.SelectedTab = 2;
					return _settingsPage;
				});

			BitcoinTabViewModel.RegisterLazy(
				() =>
				{
					_settingsPage.SelectedTab = 3;
					return _settingsPage;
				});

			AboutViewModel.RegisterLazy(() => new AboutViewModel());

			LegalDocumentsViewModel.RegisterAsyncLazy(
				async () =>
				{
					var content = await File.ReadAllTextAsync(_global.LegalDocuments.FilePath);

					var legalDocs = new LegalDocumentsViewModel(content);

					return legalDocs;
				});
		}

		private static void RegisterCategories(SearchPageViewModel searchPage)
		{
			searchPage.RegisterCategory("General", 0);
			searchPage.RegisterCategory("Settings", 1);
		}
	}
}