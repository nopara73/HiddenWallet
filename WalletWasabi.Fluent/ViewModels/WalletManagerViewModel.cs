using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Wallets;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels
{
	public partial class WalletManagerViewModel : ViewModelBase
	{
		private readonly Dictionary<Wallet, WalletViewModelBase> _walletDictionary;
		private readonly ReadOnlyObservableCollection<NavBarItemViewModel> _items;
		private NavBarItemViewModel? _currentSelection;
		[AutoNotify] private WalletViewModelBase? _selectedWallet;
		[AutoNotify(SetterModifier = AccessModifier.Private)] private bool _isLoadingWallet;
		[AutoNotify] private bool _loggedInAndSelectedAlwaysFirst;
		[AutoNotify] private ObservableCollection<WalletViewModelBase> _wallets;
		[AutoNotify] private bool _anyWalletStarted;

		public WalletManagerViewModel()
		{
			_walletDictionary = new Dictionary<Wallet, WalletViewModelBase>();
			_wallets = new ObservableCollection<WalletViewModelBase>();
			_loggedInAndSelectedAlwaysFirst = true;

			static Func<WalletViewModelBase, bool> SelectedWalletFilter(WalletViewModelBase? selected)
			{
				return item => selected is null || item != selected;
			}

			var selectedWalletFilter = this.WhenValueChanged(t => t.SelectedWallet).Select(SelectedWalletFilter);

			_wallets
				.ToObservableChangeSet()
				.Filter(selectedWalletFilter)
				.Sort(SortExpressionComparer<WalletViewModelBase>.Descending(i => i.WalletState).ThenByDescending(i => i.IsLoggedIn).ThenByAscending(i => i.Title))
				.Transform(x => x as NavBarItemViewModel)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Bind(out _items)
				.AsObservableList();

			Observable
				.FromEventPattern<WalletState>(Services.WalletManager, nameof(WalletWasabi.Wallets.WalletManager.WalletStateChanged))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(
					x =>
				{
					var wallet = x.Sender as Wallet;

					if (wallet is { } && _walletDictionary.ContainsKey(wallet))
					{
						if (wallet.State == WalletState.Stopping)
						{
							RemoveWallet(_walletDictionary[wallet]);
						}
						else if (_walletDictionary[wallet] is ClosedWalletViewModel { IsLoggedIn: true } cwvm && wallet.State == WalletState.Started)
						{
							OpenClosedWallet(cwvm);
						}
					}

					AnyWalletStarted = Items.OfType<WalletViewModelBase>().Any(y => y.WalletState == WalletState.Started);
				});

			Observable
				.FromEventPattern<Wallet>(Services.WalletManager, nameof(WalletWasabi.Wallets.WalletManager.WalletAdded))
				.Select(x => x.EventArgs)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(
					wallet =>
				{
					WalletViewModelBase vm = (wallet.State <= WalletState.Starting)
						? ClosedWalletViewModel.Create(wallet)
						: WalletViewModel.Create(wallet);

					InsertWallet(vm);
				});

			Observable
				.FromEventPattern<ProcessedResult>(Services.WalletManager, nameof(Services.WalletManager.WalletRelevantTransactionProcessed))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(async arg =>
				{
					var (sender, e) = arg;

					if (Services.UiConfig.PrivacyMode || !e.IsNews || sender is not Wallet { IsLoggedIn: true, State: WalletState.Started } wallet)
					{
						return;
					}

					if (_walletDictionary.TryGetValue(wallet, out var walletViewModel) && walletViewModel is WalletViewModel wvm)
					{
						NotificationHelpers.Show(e, onClick: () => wvm.NavigateAndHighlight(e.Transaction.GetHash()));

						if (wvm.IsSelected && (e.NewlyReceivedCoins.Any() || e.NewlyConfirmedReceivedCoins.Any()))
						{
							await Task.Delay(200);
							wvm.History.SelectTransaction(e.Transaction.GetHash());
						}
					}
				});

			RxApp.MainThreadScheduler.Schedule(() => EnumerateWallets());
		}

		public ReadOnlyObservableCollection<NavBarItemViewModel> Items => _items;

		public WalletViewModel GetWalletViewModel(Wallet wallet)
		{
			WalletViewModel? result = null;

			if (_walletDictionary.ContainsKey(wallet))
			{
				result = _walletDictionary[wallet] as WalletViewModel;
			}

			if (result is { })
			{
				return result;
			}

			throw new Exception("Wallet not found, invalid api usage");
		}

		public async Task LoadWalletAsync(Wallet wallet)
		{
			if (wallet.State != WalletState.Uninitialized)
			{
				throw new Exception("Wallet is already being logged in.");
			}

			try
			{
				await Task.Run(async () => await Services.WalletManager.StartWalletAsync(wallet));
			}
			catch (OperationCanceledException ex)
			{
				Logger.LogTrace(ex);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
			}
		}

		private void OpenClosedWallet(ClosedWalletViewModel closedWalletViewModel)
		{
			IsLoadingWallet = true;

			RemoveWallet(closedWalletViewModel);

			var walletViewModelItem = OpenWallet(closedWalletViewModel.Wallet);

			if (_currentSelection == closedWalletViewModel)
			{
				SelectedWallet = walletViewModelItem;
			}

			IsLoadingWallet = false;
		}

		private WalletViewModel OpenWallet(Wallet wallet)
		{
			if (_wallets.Any(x => x.Title == wallet.WalletName))
			{
				throw new Exception("Wallet already opened.");
			}

			var walletViewModel = WalletViewModel.Create(wallet);

			InsertWallet(walletViewModel);

			walletViewModel.IsExpanded = true;

			return walletViewModel;
		}

		private void InsertWallet(WalletViewModelBase wallet)
		{
			_wallets.InsertSorted(wallet);
			_walletDictionary.Add(wallet.Wallet, wallet);
		}

		private void RemoveWallet(WalletViewModelBase walletViewModel)
		{
			_wallets.Remove(walletViewModel);
			_walletDictionary.Remove(walletViewModel.Wallet);
		}

		private void EnumerateWallets()
		{
			foreach (var wallet in Services.WalletManager.GetWallets())
			{
				InsertWallet(ClosedWalletViewModel.Create(wallet));
			}
		}

		public NavBarItemViewModel? SelectionChanged(NavBarItemViewModel item)
		{
			if (item.SelectionMode == NavBarItemSelectionMode.Selected)
			{
				_currentSelection = item;
			}

			if (IsLoadingWallet || SelectedWallet == item)
			{
				return default;
			}

			var result = default(NavBarItemViewModel);

			if (SelectedWallet is { IsLoggedIn: true } && (item is WalletViewModelBase && SelectedWallet != item))
			{
				if (/*item is not WalletActionViewModel &&*/ SelectedWallet != item)
				{
					SelectedWallet = null;
					result = item;
				}
			}

			if (item is WalletViewModel { IsLoggedIn: true } walletViewModelItem)
			{
				SelectedWallet = walletViewModelItem;
				result = item;
			}

			return result;
		}
	}
}
