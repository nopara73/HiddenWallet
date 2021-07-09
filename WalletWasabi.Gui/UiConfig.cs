using System;
using Avalonia.Controls;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using WalletWasabi.Bases;
using WalletWasabi.Gui.Converters;
using WalletWasabi.Gui.Models.Sorting;

namespace WalletWasabi.Gui
{
	[JsonObject(MemberSerialization.OptIn)]
	public class UiConfig : ConfigBase
	{
		private bool _privacyMode;
		private bool _lockScreenActive;
		private string _lockScreenPinHash = "";
		private bool _isCustomFee;
		private bool _isCustomChangeAddress;
		private bool _autocopy;
		private int _feeDisplayFormat;
		private bool _darkModeEnabled;
		private string? _lastSelectedWallet;
		private string _windowState = "Normal";
		private bool _usePsbtWorkflow;
		private bool _oobe = true;

		public UiConfig() : base()
		{
		}

		public UiConfig(string filePath) : base(filePath)
		{
			this.WhenAnyValue(
					x => x.LockScreenPinHash,
					x => x.Autocopy,
					x => x.IsCustomFee,
					x => x.IsCustomChangeAddress,
					x => x.DarkModeEnabled,
					x => x.FeeDisplayFormat,
					x => x.LastSelectedWallet,
					x => x.WindowState,
					x=>x.Oobe,
					(_, _, _, _, _, _, _, _, _) => Unit.Default)
				.Throttle(TimeSpan.FromMilliseconds(500))
				.Skip(1) // Won't save on UiConfig creation.
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(_ => ToFile());
		}

		[JsonProperty(PropertyName = "Oobe")]
		[DefaultValue(true)]
		public bool Oobe
		{
			get => _oobe;
			set => RaiseAndSetIfChanged(ref _oobe, value);
		}

		[JsonProperty(PropertyName = "WindowState")]
		[JsonConverter(typeof(WindowStateAfterStartJsonConverter))]
		public string WindowState
		{
			get => _windowState;
			internal set => RaiseAndSetIfChanged(ref _windowState, value);
		}

		[DefaultValue(2)]
		[JsonProperty(PropertyName = "FeeTarget", DefaultValueHandling = DefaultValueHandling.Populate)]
		public int FeeTarget { get; internal set; }

		[DefaultValue(0)]
		[JsonProperty(PropertyName = "FeeDisplayFormat", DefaultValueHandling = DefaultValueHandling.Populate)]
		public int FeeDisplayFormat
		{
			get => _feeDisplayFormat;
			set => RaiseAndSetIfChanged(ref _feeDisplayFormat, value);
		}

		[DefaultValue("")]
		[JsonProperty(PropertyName = "LastActiveTab", DefaultValueHandling = DefaultValueHandling.Populate)]
		public string LastActiveTab { get; internal set; } = "";

		[DefaultValue(true)]
		[JsonProperty(PropertyName = "Autocopy", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool Autocopy
		{
			get => _autocopy;
			set => RaiseAndSetIfChanged(ref _autocopy, value);
		}

		[DefaultValue(false)]
		[JsonProperty(PropertyName = "IsCustomFee", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool IsCustomFee
		{
			get => _isCustomFee;
			set => RaiseAndSetIfChanged(ref _isCustomFee, value);
		}

		[DefaultValue(false)]
		[JsonProperty(PropertyName = "IsCustomChangeAddress", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool IsCustomChangeAddress
		{
			get => _isCustomChangeAddress;
			set => RaiseAndSetIfChanged(ref _isCustomChangeAddress, value);
		}

		[DefaultValue(false)]
		[JsonProperty(PropertyName = "PrivacyMode", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool PrivacyMode
		{
			get => _privacyMode;
			set => RaiseAndSetIfChanged(ref _privacyMode, value);
		}

		[DefaultValue(false)]
		[JsonProperty(PropertyName = "LockScreenActive", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool LockScreenActive
		{
			get => _lockScreenActive;
			set => RaiseAndSetIfChanged(ref _lockScreenActive, value);
		}

		[DefaultValue("")]
		[JsonProperty(PropertyName = "LockScreenPinHash", DefaultValueHandling = DefaultValueHandling.Populate)]
		public string LockScreenPinHash
		{
			get => _lockScreenPinHash;
			set => RaiseAndSetIfChanged(ref _lockScreenPinHash, value);
		}

		[DefaultValue(true)]
		[JsonProperty(PropertyName = "DarkModeEnabled", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool DarkModeEnabled
		{
			get => _darkModeEnabled;
			set => RaiseAndSetIfChanged(ref _darkModeEnabled, value);
		}

		[DefaultValue(null)]
		[JsonProperty(PropertyName = "LastSelectedWallet", DefaultValueHandling = DefaultValueHandling.Populate)]
		public string? LastSelectedWallet
		{
			get => _lastSelectedWallet;
			set => RaiseAndSetIfChanged(ref _lastSelectedWallet, value);
		}

		[JsonProperty(PropertyName = "CoinListViewSortingPreference")]
		[JsonConverter(typeof(SortingPreferenceJsonConverter))]
		public SortingPreference CoinListViewSortingPreference { get; internal set; } = new SortingPreference(SortOrder.Increasing, "Amount");

		[JsonProperty(PropertyName = "CoinJoinTabSortingPreference")]
		[JsonConverter(typeof(SortingPreferenceJsonConverter))]
		public SortingPreference CoinJoinTabSortingPreference { get; internal set; } = new SortingPreference(SortOrder.Increasing, "Amount");

		[JsonProperty(PropertyName = "HistoryTabViewSortingPreference")]
		[JsonConverter(typeof(SortingPreferenceJsonConverter))]
		public SortingPreference HistoryTabViewSortingPreference { get; internal set; } = new SortingPreference(SortOrder.Decreasing, "Date");
	}
}
