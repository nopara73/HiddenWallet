using System.Collections.Generic;
using System.Linq;
using ReactiveUI;
using WalletWasabi.Gui.ViewModels;

namespace WalletWasabi.Fluent.ViewModels.Navigation
{
	public class NavigationStack<T> : ViewModelBase, INavigationStack<T> where T : class, INavigatable
	{
		private readonly Stack<T> _backStack;
		private T? _currentPage;
		private bool _canNavigateBack;
		private bool _operationsEnabled = true;

		public NavigationStack()
		{
			_backStack = new Stack<T>();
		}

		public T? CurrentPage
		{
			get => _currentPage;
			set => this.RaiseAndSetIfChanged(ref _currentPage, value);
		}

		public bool CanNavigateBack
		{
			get => _canNavigateBack;
			set => this.RaiseAndSetIfChanged(ref _canNavigateBack, value);
		}

		protected virtual void OnNavigated(T? oldPage, bool oldInStack, T? newPage, bool newInStack)
		{
		}

		protected virtual void OnPopped(T page)
		{
		}

		private void NavigationOperation(T? oldPage, bool oldInStack, T? newPage, bool newInStack)
		{
			if (_operationsEnabled)
			{
				oldPage?.OnNavigatedFrom(oldInStack);
			}

			CurrentPage = newPage;

			if (!oldInStack && oldPage is { })
			{
				OnPopped(oldPage);
			}

			if (_operationsEnabled)
			{
				OnNavigated(oldPage, oldInStack, newPage, newInStack);
			}

			if (_operationsEnabled && newPage is { })
			{
				newPage.OnNavigatedTo(newInStack);
			}

			UpdateCanNavigateBack();
		}

		protected IEnumerable<T> Stack => _backStack;

		public virtual void Clear()
		{
			Clear(false);
		}

		protected virtual void Clear(bool keepRoot)
		{
			var root = _backStack.Count > 0 ? _backStack.Last() : CurrentPage;

			if ((keepRoot && CurrentPage == root) || (!keepRoot && _backStack.Count == 0 && CurrentPage is null))
			{
				return;
			}

			var oldPage = CurrentPage;

			var oldItems = _backStack.ToList();

			_backStack.Clear();

			if (keepRoot)
			{
				foreach (var item in oldItems)
				{
					if (item != root)
					{
						OnPopped(item);
					}
				}
				CurrentPage = root;
			}
			else
			{
				foreach (var item in oldItems)
				{
					OnPopped(item);
				}

				CurrentPage = null;
			}

			NavigationOperation(oldPage, false, CurrentPage, CurrentPage is { });
		}

		public void BackTo(T viewmodel)
		{
			if (CurrentPage == viewmodel)
			{
				return;
			}

			if (_backStack.Contains(viewmodel))
			{
				var oldPage = CurrentPage;

				while (_backStack.Pop() != viewmodel)
				{
				}

				NavigationOperation(oldPage, false, viewmodel, true);
			}
		}

		public void To(T viewmodel, NavigationMode mode = NavigationMode.Normal)
		{
			var oldPage = CurrentPage;

			bool oldInStack = true;
			bool newInStack = false;

			switch (mode)
			{
				case NavigationMode.Normal:
					if (oldPage is { })
					{
						_backStack.Push(oldPage);
					}
					break;

				case NavigationMode.Clear:
					oldInStack = false;
					_operationsEnabled = false;
					Clear();
					_operationsEnabled = true;

					if (CurrentPage is { })
					{
						_backStack.Push(CurrentPage);
					}
					break;
			}

			NavigationOperation(oldPage, oldInStack, viewmodel, newInStack);
		}

		public void Back()
		{
			if (_backStack.Count > 0)
			{
				var oldPage = CurrentPage;

				CurrentPage = _backStack.Pop();

				NavigationOperation(oldPage, false, CurrentPage, true);
			}
			else
			{
				Clear(); // in this case only CurrentPage might be set and Clear will provide correct behavior.
			}
		}

		private void UpdateCanNavigateBack()
		{
			CanNavigateBack = _backStack.Count > 0;
		}
	}
}