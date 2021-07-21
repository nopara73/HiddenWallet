using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Home.History
{
	public partial class HistoryItemViewModel : ViewModelBase
	{
		[AutoNotify] private bool _isFlashing;
		[AutoNotify] private bool _isConfirmed;
		[AutoNotify] private int _orderIndex;
		[AutoNotify] private DateTimeOffset _date;

		public HistoryItemViewModel(int orderIndex, TransactionSummary transactionSummary, WalletViewModel walletViewModel, Money balance, IObservable<Unit> updateTrigger)
		{
			TransactionSummary = transactionSummary;
			Date = transactionSummary.DateTime.ToLocalTime();
			IsCoinJoin = transactionSummary.IsLikelyCoinJoinOutput;
			OrderIndex = orderIndex;
			Balance = balance;

			var confirmations = transactionSummary.Height.Type == HeightType.Chain ? (int) Services.BitcoinStore.SmartHeaderChain.TipHeight - transactionSummary.Height.Value + 1 : 0;
			IsConfirmed = confirmations > 0;

			var amount = transactionSummary.Amount;
			if (amount < Money.Zero)
			{
				OutgoingAmount = amount * -1;
			}
			else
			{
				IncomingAmount = amount;
			}

			Label = transactionSummary.Label.Take(1).ToList();
			FilteredLabel = transactionSummary.Label.Skip(1).ToList();

			ShowDetailsCommand = ReactiveCommand.Create(() => RoutableViewModel.Navigate(NavigationTarget.DialogScreen).To(new TransactionDetailsViewModel(transactionSummary, walletViewModel.Wallet, updateTrigger)));

			this.WhenAnyValue(x => x.IsFlashing)
				.Where(x => x)
				.Subscribe(async _ =>
				{
					await Task.Delay(1260);
					IsFlashing = false;
				});
		}

		public ICommand ShowDetailsCommand { get; }

		public TransactionSummary TransactionSummary { get; }

		public Money Balance { get; set; }

		public Money? IncomingAmount { get; }

		public Money? OutgoingAmount { get; }

		public List<string> FilteredLabel { get; }

		public List<string> Label { get; }

		public bool IsCoinJoin { get; }

		public void Update(HistoryItemViewModel item)
		{
			OrderIndex = item.OrderIndex;
			Date = item.TransactionSummary.DateTime.ToLocalTime();
			var confirmations = item.TransactionSummary.Height.Type == HeightType.Chain ? (int) Services.BitcoinStore.SmartHeaderChain.TipHeight - item.TransactionSummary.Height.Value + 1 : 0;
			IsConfirmed = confirmations > 0;
		}
	}
}
