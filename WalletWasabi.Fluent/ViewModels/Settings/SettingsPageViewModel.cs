using System.Reactive.Disposables;
using ReactiveUI;
using WalletWasabi.Fluent.Model;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Gui;

namespace WalletWasabi.Fluent.ViewModels.Settings
{
	[NavigationMetaData(
		Title = "Settings",
		Caption = "Manage appearance, privacy and other settings",
		Order = 1,
		Category = "General",
		Keywords = new[] { "Settings", "General", "User", "Interface", "Privacy", "Advanced" },
		IconName = "settings_regular",
		NavBarPosition = NavBarPosition.Bottom)]
	public partial class SettingsPageViewModel : NavBarItemViewModel
	{
		[AutoNotify] private bool _isModified;
		[AutoNotify] private int _selectedTab;

		public SettingsPageViewModel(Config config, UiConfig uiConfig)
		{
			Title = "Settings";

			_selectedTab = 0;

			GeneralSettingsTab = new GeneralSettingsTabViewModel(config, uiConfig);
			PrivacySettingsTab = new PrivacySettingsTabViewModel(config, uiConfig);
			NetworkSettingsTab = new NetworkSettingsTabViewModel(config, uiConfig);
			BitcoinTabSettings = new BitcoinTabSettingsViewModel(config, uiConfig);
		}

		public GeneralSettingsTabViewModel GeneralSettingsTab { get; }
		public PrivacySettingsTabViewModel PrivacySettingsTab { get; }
		public NetworkSettingsTabViewModel NetworkSettingsTab { get; }
		public BitcoinTabSettingsViewModel BitcoinTabSettings { get; }

		private void OnRestartNeeded(object? sender, RestartNeededEventArgs e)
		{
			IsModified = e.IsRestartNeeded;
		}

		protected override void OnNavigatedTo(bool inStack, CompositeDisposable disposable)
		{
			base.OnNavigatedTo(inStack, disposable);

			SettingsTabViewModelBase.RestartNeeded += OnRestartNeeded;

			Disposable.Create(() => { SettingsTabViewModelBase.RestartNeeded -= OnRestartNeeded; })
				.DisposeWith(disposable);
		}
	}
}