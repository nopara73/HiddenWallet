using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ReactiveUI;

namespace WalletWasabi.Fluent.Behaviors
{
	public class FocusNextWhenValid : DisposingBehavior<TextBox>
	{
		protected override void OnAttached(CompositeDisposable disposables)
		{
			var hasErrors = AssociatedObject.GetObservable(DataValidationErrors.HasErrorsProperty);
			var text = AssociatedObject.GetObservable(TextBox.TextProperty);

			hasErrors.Select(_ => Unit.Default)
				.Merge(text.Select(_ => Unit.Default))
				.Throttle(TimeSpan.FromMilliseconds(100))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ =>
				{
					if (AssociatedObject is { } &&
					    !DataValidationErrors.GetHasErrors(AssociatedObject) &&
					    !string.IsNullOrEmpty(AssociatedObject.Text) &&
					    KeyboardNavigationHandler.GetNext(AssociatedObject, NavigationDirection.Next) is { } nextFocus)
					{
						nextFocus.Focus();
					}
				})
				.DisposeWith(disposables);
		}
	}
}
