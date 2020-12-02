using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Dialogs;
using WalletWasabi.Gui.ViewModels;

namespace WalletWasabi.Fluent.ViewModels.Navigation
{
	public abstract partial class RoutableViewModel : ViewModelBase, INavigatable
	{
		[AutoNotify] private bool _isBusy;
		private CompositeDisposable? _currentDisposable;

		public NavigationTarget CurrentTarget { get; internal set; }

		public virtual NavigationTarget DefaultTarget => NavigationTarget.HomeScreen;

		protected RoutableViewModel()
		{
			BackCommand = ReactiveCommand.Create(() => Navigate().Back());

			CancelCommand = ReactiveCommand.Create(() => Navigate().Clear());
		}

		public ICommand? NextCommand { get; protected set; }

		public ICommand BackCommand { get; protected set; }

		public ICommand CancelCommand { get; protected set; }

		private void DoNavigateTo(bool inStack)
		{
			if (_currentDisposable is { })
			{
				throw new Exception("Cant navigate to something that has already been navigated to.");
			}

			_currentDisposable = new CompositeDisposable();

			OnNavigatedTo(inStack, _currentDisposable);
		}

		protected virtual void OnNavigatedTo(bool inStack, CompositeDisposable disposable)
		{
		}

		private void DoNavigateFrom()
		{
			OnNavigatedFrom();

			_currentDisposable?.Dispose();
			_currentDisposable = null;
		}

		public INavigationStack<RoutableViewModel> Navigate()
		{
			var currentTarget = CurrentTarget == NavigationTarget.Default ? DefaultTarget : CurrentTarget;

			return Navigate(currentTarget);
		}

		public INavigationStack<RoutableViewModel> Navigate(NavigationTarget currentTarget)
		{
			switch (currentTarget)
			{
				case NavigationTarget.HomeScreen:
					return NavigationState.Instance.HomeScreenNavigation;

				case NavigationTarget.DialogScreen:
					return NavigationState.Instance.DialogScreenNavigation;
			}

			throw new NotSupportedException();
		}

		public void OnNavigatedTo(bool isInHistory)
		{
			DoNavigateTo(isInHistory);
		}

		void INavigatable.OnNavigatedFrom(bool isInHistory)
		{
			DoNavigateFrom();
		}

		protected virtual void OnNavigatedFrom()
		{
		}

		public async Task<TResult> NavigateDialog<TResult>(DialogViewModelBase<TResult> dialog)
			=> await NavigateDialog(dialog, CurrentTarget);

		public async Task<TResult> NavigateDialog<TResult>(DialogViewModelBase<TResult> dialog, NavigationTarget target)
		{
			Navigate(target).To(dialog);

			var result = await dialog.GetDialogResultAsync();

			Navigate(target).Back();

			return result;
		}
	}
}
