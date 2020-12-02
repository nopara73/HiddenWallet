using ReactiveUI;
using WalletWasabi.Gui.Validation;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Models;

namespace WalletWasabi.Fluent.ViewModels.AddWallet
{
	public partial class RecoveryWordViewModel : ViewModelBase
	{
		[AutoNotify] private string? _input;
		[AutoNotify] private bool _isConfirmed;

		public RecoveryWordViewModel(int index, string word)
		{
			Index = index;
			Word = word;

			this.ValidateProperty(x => x.Input, ValidateWord);
		}

		public int Index { get; }
		public string Word { get; }

		public void Reset()
		{
			Input = "";
			IsConfirmed = false;
		}

		private void ValidateWord(IValidationErrors errors)
		{
			if (string.IsNullOrWhiteSpace(Input))
			{
				return;
			}

			if (Input == Word)
			{
				IsConfirmed = true;
				return;
			}

			errors.Add(ErrorSeverity.Error, $"The input does not match recovery word {Index}.");
		}

		public override string ToString()
		{
			return $"{Index}. {Word}";
		}
	}
}