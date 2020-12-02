using System.Reactive.Linq;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Gui.Validation;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.Dialogs
{
	public partial class AdvancedRecoveryOptionsViewModel : DialogViewModelBase<(KeyPath? accountKeyPath, int? minGapLimit)>
	{
		[AutoNotify] private string _accountKeyPath;
		[AutoNotify] private string _minGapLimit;

		public AdvancedRecoveryOptionsViewModel((KeyPath keyPath, int minGapLimit) interactionInput)
		{
			this.ValidateProperty(x => x.AccountKeyPath, ValidateAccountKeyPath);
			this.ValidateProperty(x => x.MinGapLimit, ValidateMinGapLimit);

			var backCommandCanExecute = this.WhenAnyValue(x => x.IsDialogOpen).ObserveOn(RxApp.MainThreadScheduler);

			var nextCommandCanExecute = this.WhenAnyValue(
					x => x.IsDialogOpen,
					x => x.AccountKeyPath,
					x => x.MinGapLimit,
					delegate
					{
						// This will fire validations before return canExecute value.
						this.RaisePropertyChanged(nameof(AccountKeyPath));
						this.RaisePropertyChanged(nameof(MinGapLimit));

						return IsDialogOpen && !Validations.Any;
					})
				.ObserveOn(RxApp.MainThreadScheduler);

			var cancelCommandCanExecute = this.WhenAnyValue(x => x.IsDialogOpen).ObserveOn(RxApp.MainThreadScheduler);

			_accountKeyPath = interactionInput.keyPath.ToString();
			_minGapLimit = interactionInput.minGapLimit.ToString();

			BackCommand = ReactiveCommand.Create(() => Navigate().Back(), backCommandCanExecute);

			NextCommand = ReactiveCommand.Create(
				() => Close((KeyPath.Parse(AccountKeyPath), int.Parse(MinGapLimit))),
				nextCommandCanExecute);

			CancelCommand = ReactiveCommand.Create(() => Close(), cancelCommandCanExecute);
		}

		private void ValidateMinGapLimit(IValidationErrors errors)
		{
			if (!int.TryParse(MinGapLimit, out var minGapLimit) || minGapLimit < KeyManager.AbsoluteMinGapLimit ||
				minGapLimit > KeyManager.MaxGapLimit)
			{
				errors.Add(
					ErrorSeverity.Error,
					$"Must be a number between {KeyManager.AbsoluteMinGapLimit} and {KeyManager.MaxGapLimit}.");
			}
		}

		private void ValidateAccountKeyPath(IValidationErrors errors)
		{
			if (KeyPath.TryParse(AccountKeyPath, out var keyPath) && keyPath is { })
			{
				var accountKeyPath = keyPath.GetAccountKeyPath();
				if (keyPath.Length != accountKeyPath.Length ||
					accountKeyPath.Length != KeyManager.DefaultAccountKeyPath.Length)
				{
					errors.Add(ErrorSeverity.Error, "Path is not a compatible account derivation path.");
				}
			}
			else
			{
				errors.Add(ErrorSeverity.Error, "Path is not a valid derivation path.");
			}
		}

		protected override void OnDialogClosed()
		{
		}
	}
}
