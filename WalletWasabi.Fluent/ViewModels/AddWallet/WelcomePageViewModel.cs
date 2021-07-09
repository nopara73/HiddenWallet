using System.Reactive;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.AddWallet
{
	public partial class WelcomePageViewModel : DialogViewModelBase<Unit>
	{
		public WelcomePageViewModel()
		{
			GetStartedCommand = ReactiveCommand.Create(() => Close());
		}

		public ICommand GetStartedCommand { get; }

		public override string Title { get; protected set; }
	}
}