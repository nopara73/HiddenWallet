using ReactiveUI;
using System.Windows.Input;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.NavBar
{
	public enum NavBarItemSelectionMode
	{
		Selected = 0,
		Button = 1,
		Toggle = 2
	}

	public abstract partial class NavBarItemViewModel : RoutableViewModel
	{
		private bool _isSelected;
		private bool _isExpanded;

		[AutoNotify] private string _title;

		protected NavBarItemViewModel()
		{
			_title = "";
			SelectionMode = NavBarItemSelectionMode.Selected;
			OpenCommand = ReactiveCommand.Create(
				() =>
				{
					Navigate().To(this, NavigationMode.Clear);
				});
		}

		public NavBarItemViewModel? Parent { get; set; }

		public abstract string IconName { get; }

		public NavBarItemSelectionMode SelectionMode { get; protected set; }

		public bool IsExpanded
		{
			get => _isExpanded;
			set
			{
				this.RaiseAndSetIfChanged(ref _isExpanded, value);

				if (Parent != null)
				{
					Parent.IsExpanded = value;
				}
			}
		}

		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				switch (SelectionMode)
				{
					case NavBarItemSelectionMode.Selected:
						this.RaiseAndSetIfChanged(ref _isSelected, value);
						break;
					case NavBarItemSelectionMode.Button:
					case NavBarItemSelectionMode.Toggle:
						break;
				}
			}
		}

		public ICommand OpenCommand { get; protected set; }

		public virtual void Toggle()
		{
		}
	}
}