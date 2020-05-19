using NBitcoin;
using NBitcoin.Payment;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Gui.Helpers;
using WalletWasabi.Gui.Models.StatusBarStatuses;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Hwi;
using WalletWasabi.Models;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.PayJoin;
using WalletWasabi.Gui.Validation;
using WalletWasabi.Interfaces;

namespace WalletWasabi.Gui.Controls.WalletExplorer
{
	public class SendTabViewModel : SendControlViewModel
	{
		private string _payjoinEndPoint;

		public SendTabViewModel(Wallet wallet) : base(wallet, "Send")
		{
			this.ValidateProperty(x => x.PayjoinEndPoint, ValidatePayjoinEndPoint);
		}

		public override string DoButtonText => "Send Transaction";
		public override string DoingButtonText => "Sending Transaction...";
		public string PayjoinEndPoint
		{
			get => _payjoinEndPoint;
			set => this.RaiseAndSetIfChanged(ref _payjoinEndPoint, value);
		}
		protected override async Task BuildTransaction(string password, PaymentIntent payments, FeeStrategy feeStrategy, bool allowUnconfirmed = false, IEnumerable<OutPoint> allowedInputs = null)
		{
			BuildTransactionResult result = await Task.Run(() => Wallet.BuildTransaction(Password, payments, feeStrategy, allowUnconfirmed: true, allowedInputs: allowedInputs, GetPayjoinClient(), GetSigner()));

			MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType.SigningTransaction);
			SmartTransaction signedTransaction = result.Transaction;
			MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType.BroadcastingTransaction);
			await Task.Run(async () => await Global.TransactionBroadcaster.SendTransactionAsync(signedTransaction));

			ResetUi();
		}

		private IPayjoinClient GetPayjoinClient()
		{
			if (!string.IsNullOrWhiteSpace(PayjoinEndPoint) && Uri.IsWellFormedUriString(PayjoinEndPoint, UriKind.Absolute))
			{
				var payjoinEndPointUri = new Uri(PayjoinEndPoint);
				return new PayjoinClient(payjoinEndPointUri, Global.TorManager.TorSocks5EndPoint);
			}

			return null;
		}

		public void ValidatePayjoinEndPoint(IValidationErrors errors)
		{
			if (!string.IsNullOrWhiteSpace(PayjoinEndPoint) && !Uri.IsWellFormedUriString(PayjoinEndPoint, UriKind.Absolute))
			{
				errors.Add(ErrorSeverity.Error, "Invalid url.");
			}
		}

		protected override void OnAddressPaste(BitcoinUrlBuilder url)
		{
			base.OnAddressPaste(url);

			if (url.UnknowParameters.TryGetValue("pj", out var endPoint))
			{
				if (!Wallet.KeyManager.IsWatchOnly)
				{
					PayjoinEndPoint = endPoint;
					return;
				}
				NotificationHelpers.Warning("Payjoin is not allowed here.");
			}
			PayjoinEndPoint = null;
		}

		protected override void ResetUi()
		{
			base.ResetUi();
			PayjoinEndPoint = "";
		}

		private IPsbtSigner GetSigner()
		{
			return Wallet.KeyManager.IsHardwareWallet
				? new HwiPsbtSigner(new HwiClient(Global.Network), b => IsHardwareBusy = b)
				: null;
		}
	}
}
