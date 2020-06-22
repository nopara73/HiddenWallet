using Avalonia;
using Avalonia.Threading;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Gui.Helpers;
using WalletWasabi.Gui.Suggestions;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace WalletWasabi.Gui.Controls.WalletExplorer
{
	public class ReceiveTabViewModel : WasabiDocumentTabViewModel, IWalletViewModel
	{
		private ObservableCollection<AddressViewModel> _addresses;
		private AddressViewModel _selectedAddress;

		public ReceiveTabViewModel(Wallet wallet)
			: base("Receive")
		{
			Global = Locator.Current.GetService<Global>();
			Wallet = wallet;

			LabelSuggestion = new SuggestLabelViewModel();
			_addresses = new ObservableCollection<AddressViewModel>();
			LabelSuggestion.Label = "";

			InitializeAddresses();

			GenerateCommand = ReactiveCommand.Create(() =>
			{
				var label = new SmartLabel(LabelSuggestion.Label);
				LabelSuggestion.Label = label;
				if (label.IsEmpty)
				{
					NotificationHelpers.Warning("Observers are required.");
					return;
				}

				AvaloniaThreadingExtensions.PostLogException(Dispatcher.UIThread, () =>
				{
					var newKey = Wallet.KeyManager.GetNextReceiveKey(label, out bool minGapLimitIncreased);
					if (minGapLimitIncreased)
					{
						int minGapLimit = Wallet.KeyManager.MinGapLimit.Value;
						int prevMinGapLimit = minGapLimit - 1;
						NotificationHelpers.Warning($"{nameof(KeyManager.MinGapLimit)} increased from {prevMinGapLimit} to {minGapLimit}.");
					}

					var newAddress = new AddressViewModel(newKey, Wallet.KeyManager, this);
					Addresses.Insert(0, newAddress);
					SelectedAddress = newAddress;
					LabelSuggestion.Label = "";
				});
			});

			LabelSuggestion.GenerateCommand = GenerateCommand;

			this.WhenAnyValue(x => x.SelectedAddress)
				.Subscribe(async address =>
				{
					if (!Global.UiConfig.Autocopy || address is null)
					{
						return;
					}

					await address.TryCopyToClipboardAsync();
				});

			Observable
				.Merge(GenerateCommand.ThrownExceptions)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(ex =>
				{
					Logger.LogError(ex);
					NotificationHelpers.Error(ex.ToUserFriendlyString());
				});
		}

		private Global Global { get; }

		public ReactiveCommand<Unit, Unit> GenerateCommand { get; }

		private Wallet Wallet { get; }

		Wallet IWalletViewModel.Wallet => Wallet;

		public SuggestLabelViewModel LabelSuggestion { get; }

		public bool IsHardwareWallet => Wallet.KeyManager.IsHardwareWallet;

		public ObservableCollection<AddressViewModel> Addresses
		{
			get => _addresses;
			set => this.RaiseAndSetIfChanged(ref _addresses, value);
		}

		public AddressViewModel SelectedAddress
		{
			get => _selectedAddress;
			set => this.RaiseAndSetIfChanged(ref _selectedAddress, value);
		}

		public override void OnOpen(CompositeDisposable disposables)
		{
			base.OnOpen(disposables);

			Observable
				.FromEventPattern(Wallet.TransactionProcessor, nameof(Wallet.TransactionProcessor.WalletRelevantTransactionProcessed))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => InitializeAddresses())
				.DisposeWith(disposables);
		}

		public void InitializeAddresses()
		{
			try
			{
				_addresses?.Clear();

				IEnumerable<HdPubKey> keys = Wallet.KeyManager.GetKeys(x => !x.Label.IsEmpty && !x.IsInternal && x.KeyState == KeyState.Clean).Reverse();
				foreach (HdPubKey key in keys)
				{
					_addresses.Add(new AddressViewModel(key, Wallet.KeyManager, this));
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
			}
		}
	}
}
