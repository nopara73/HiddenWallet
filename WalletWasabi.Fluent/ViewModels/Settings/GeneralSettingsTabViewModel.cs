using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Gui;
using WalletWasabi.Gui.Models;
using WalletWasabi.Gui.Validation;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.Settings
{
	[NavigationMetaData(
		Title = "General",
		Caption = "Manage general settings",
		Order = 0,
		Category = "Settings",
		Keywords = new[]
		{
			"Settings", "General", "Dark", "Mode", "Bitcoin", "Addresses", "Manual", "Entry", "Fee", "Custom", "Change",
			"Address", "Display", "Format", "Dust", "Threshold", "BTC"
		},
		IconName = "settings_general_regular")]
	public partial class GeneralSettingsTabViewModel : SettingsTabViewModelBase
	{
		[AutoNotify] private bool _darkModeEnabled;
		[AutoNotify] private bool _autoCopy;
		[AutoNotify] private bool _customFee;
		[AutoNotify] private bool _customChangeAddress;
		[AutoNotify] private FeeDisplayFormat _selectedFeeDisplayFormat;
		[AutoNotify] private string _dustThreshold;

		public GeneralSettingsTabViewModel(Config config, UiConfig uiConfig) : base(config, uiConfig)
		{
			this.ValidateProperty(x => x.DustThreshold, ValidateDustThreshold);

			_darkModeEnabled = uiConfig.DarkModeEnabled;
			_autoCopy = uiConfig.Autocopy;
			_customFee = uiConfig.IsCustomFee;
			_customChangeAddress = uiConfig.IsCustomChangeAddress;
			_selectedFeeDisplayFormat = Enum.IsDefined(typeof(FeeDisplayFormat), uiConfig.FeeDisplayFormat)
				? (FeeDisplayFormat) uiConfig.FeeDisplayFormat
				: FeeDisplayFormat.SatoshiPerByte;
			_dustThreshold = config.DustThreshold.ToString();

			this.WhenAnyValue(x => x.DustThreshold)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Throttle(TimeSpan.FromMilliseconds(ThrottleTime))
				.Skip(1)
				.Subscribe(_ => Save());

			this.WhenAnyValue(x => x.DarkModeEnabled)
				.Skip(1)
				.Subscribe(
					x =>
					{
						uiConfig.DarkModeEnabled = x;
						IsRestartNeeded(x);
					});

			this.WhenAnyValue(x => x.AutoCopy)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Skip(1)
				.Subscribe(x => uiConfig.Autocopy = x);

			this.WhenAnyValue(x => x.CustomFee)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Skip(1)
				.Subscribe(x => uiConfig.IsCustomFee = x);

			this.WhenAnyValue(x => x.CustomChangeAddress)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Skip(1)
				.Subscribe(x => uiConfig.IsCustomChangeAddress = x);

			this.WhenAnyValue(x => x.SelectedFeeDisplayFormat)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Skip(1)
				.Subscribe(x => uiConfig.FeeDisplayFormat = (int) x);
		}

		public IEnumerable<FeeDisplayFormat> FeeDisplayFormats =>
			Enum.GetValues(typeof(FeeDisplayFormat)).Cast<FeeDisplayFormat>();

		private void ValidateDustThreshold(IValidationErrors errors) =>
			ValidateDustThreshold(errors, DustThreshold, whiteSpaceOk: true);

		private void ValidateDustThreshold(IValidationErrors errors, string dustThreshold, bool whiteSpaceOk)
		{
			if (!whiteSpaceOk || !string.IsNullOrWhiteSpace(dustThreshold))
			{
				if (!string.IsNullOrEmpty(dustThreshold) && dustThreshold.Contains(
					',',
					StringComparison.InvariantCultureIgnoreCase))
				{
					errors.Add(ErrorSeverity.Error, "Use decimal point instead of comma.");
				}

				if (!decimal.TryParse(dustThreshold, out var dust) || dust < 0)
				{
					errors.Add(ErrorSeverity.Error, "Invalid dust threshold.");
				}
			}
		}

		protected override void EditConfigOnSave(Config config)
		{
			config.DustThreshold = decimal.TryParse(DustThreshold, out var threshold)
				? Money.Coins(threshold)
				: Config.DefaultDustThreshold;
		}
	}
}