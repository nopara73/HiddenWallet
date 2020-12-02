using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.Dialogs
{
	/// <summary>
	/// CommonBase class.
	/// </summary>
	public abstract partial class DialogViewModelBase : RoutableViewModel
	{
		[AutoNotify] private bool _isDialogOpen;
	}
}