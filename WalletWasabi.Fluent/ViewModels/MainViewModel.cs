using System;
using System.Reactive.Concurrency;
using NBitcoin;
using ReactiveUI;
using System.Reactive.Linq;
using Avalonia.Controls;
using WalletWasabi.Fluent.ViewModels.AddWallet;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.Search;
using WalletWasabi.Fluent.ViewModels.Settings;
using WalletWasabi.Fluent.ViewModels.TransactionBroadcasting;
using WalletWasabi.Fluent.ViewModels.HelpAndSupport;
using WalletWasabi.Fluent.ViewModels.OpenDirectory;
using WalletWasabi.Logging;
using WalletWasabi.Fluent.ViewModels.StatusBar;

namespace WalletWasabi.Fluent.ViewModels
{
	public partial class MainViewModel : ViewModelBase
	{
		private readonly SettingsPageViewModel _settingsPage;
		private readonly SearchPageViewModel _searchPage;
		private readonly PrivacyModeViewModel _privacyMode;
		private readonly AddWalletPageViewModel _addWalletPage;
		[AutoNotify] private bool _isMainContentEnabled;
		[AutoNotify] private bool _isDialogScreenEnabled;
		[AutoNotify] private bool _isFullScreenEnabled;
		[AutoNotify] private DialogScreenViewModel _dialogScreen;
		[AutoNotify] private DialogScreenViewModel _fullScreen;
		[AutoNotify] private DialogScreenViewModel _compactDialogScreen;
		[AutoNotify] private NavBarViewModel _navBar;
		[AutoNotify] private StatusBarViewModel _statusBar;
		[AutoNotify] private string _title = "Wasabi Wallet";
		[AutoNotify] private WindowState _windowState;
		[AutoNotify] private bool _isSetup;

		public MainViewModel()
		{
			_windowState = (WindowState)Enum.Parse(typeof(WindowState), Services.UiConfig.WindowState);
			_dialogScreen = new DialogScreenViewModel();

			_fullScreen = new DialogScreenViewModel(NavigationTarget.FullScreen);

			_compactDialogScreen = new DialogScreenViewModel(NavigationTarget.CompactDialogScreen);

			MainScreen = new TargettedNavigationStack(NavigationTarget.HomeScreen);

			NavigationState.Register(MainScreen, DialogScreen, FullScreen, CompactDialogScreen);

			_isMainContentEnabled = true;
			_isDialogScreenEnabled = true;
			_isFullScreenEnabled = true;

			_statusBar = new StatusBarViewModel();

			UiServices.Initialize();

			_addWalletPage = new AddWalletPageViewModel();
			_settingsPage = new SettingsPageViewModel();
			_privacyMode = new PrivacyModeViewModel();
			_searchPage = new SearchPageViewModel();
			_navBar = new NavBarViewModel(MainScreen);

			NavigationManager.RegisterType(_navBar);

			RegisterCategories(_searchPage);
			RegisterViewModels();

			RxApp.MainThreadScheduler.Schedule(async () => await _navBar.InitialiseAsync());

			_searchPage.Initialise();

			this.WhenAnyValue(x => x.WindowState)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(windowState => Services.UiConfig.WindowState = windowState.ToString());

			this.WhenAnyValue(x => x.DialogScreen!.IsDialogOpen)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x => IsMainContentEnabled = !x);

			this.WhenAnyValue(x => x.FullScreen!.IsDialogOpen)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x => IsMainContentEnabled = !x);

			this.WhenAnyValue(x => x.CompactDialogScreen!.IsDialogOpen)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x => IsMainContentEnabled = !x);

			IsSetup = !Services.UiConfig.Oobe;

			RxApp.MainThreadScheduler.Schedule(async () =>
			{
				if (!Services.WalletManager.HasWallet() || Services.UiConfig.Oobe)
				{
					IsSetup = false;

					await _dialogScreen.NavigateDialogAsync(new WelcomePageViewModel());

					Services.UiConfig.Oobe = false;
					IsSetup = true;
				}

				if (!Services.WalletManager.HasWallet())
				{
					_dialogScreen.To(_addWalletPage, NavigationMode.Clear);
				}
			});
		}

		public TargettedNavigationStack MainScreen { get; }

		public static MainViewModel? Instance { get; internal set; }

		public void ClearStacks()
		{
			MainScreen.Clear();
			DialogScreen.Clear();
			FullScreen.Clear();
		}

		public void Initialize()
		{
			StatusBar.Initialize();

			if (Services.Config.Network != Network.Main)
			{
				Title += $" - {Services.Config.Network}";
			}
		}

		private void RegisterViewModels()
		{
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

			BitcoinTabSettingsViewModel.RegisterLazy(
				() =>
				{
					_settingsPage.SelectedTab = 3;
					return _settingsPage;
				});

			AboutViewModel.RegisterLazy(() => new AboutViewModel());

			BroadcastTransactionViewModel.RegisterAsyncLazy(
				async () =>
				{
					var dialogResult = await DialogScreen.NavigateDialogAsync(new LoadTransactionViewModel(Services.Config.Network));

					if (dialogResult.Result is { })
					{
						return new BroadcastTransactionViewModel(Services.Config.Network,
							dialogResult.Result);
					}

					return null;
				});

			RxApp.MainThreadScheduler.Schedule(async () =>
			{
				try
				{
					await Services.LegalChecker.WaitAndGetLatestDocumentAsync();

					LegalDocumentsViewModel.RegisterAsyncLazy(async () =>
					{
						var document = await Services.LegalChecker.WaitAndGetLatestDocumentAsync();
						return new LegalDocumentsViewModel(document.Content);
					});
					_searchPage.RegisterSearchEntry(LegalDocumentsViewModel.MetaData);
				}
				catch (Exception ex)
				{
					if (ex is not OperationCanceledException)
					{
						Logger.LogError("Failed to get Legal documents.", ex);
					}
				}
			});

			UserSupportViewModel.RegisterLazy(() => new UserSupportViewModel());
			BugReportLinkViewModel.RegisterLazy(() => new BugReportLinkViewModel());
			DocsLinkViewModel.RegisterLazy(() => new DocsLinkViewModel());
			OpenDataFolderViewModel.RegisterLazy(() => new OpenDataFolderViewModel());
			OpenWalletsFolderViewModel.RegisterLazy(() => new OpenWalletsFolderViewModel());
			OpenLogsViewModel.RegisterLazy(() => new OpenLogsViewModel());
			OpenTorLogsViewModel.RegisterLazy(() => new OpenTorLogsViewModel());
			OpenConfigFileViewModel.RegisterLazy(() => new OpenConfigFileViewModel());
		}

		private static void RegisterCategories(SearchPageViewModel searchPage)
		{
			searchPage.RegisterCategory("General", 0);
			searchPage.RegisterCategory("Settings", 1);
			searchPage.RegisterCategory("Help & Support", 2);
			searchPage.RegisterCategory("Open", 3);
		}
	}
}
