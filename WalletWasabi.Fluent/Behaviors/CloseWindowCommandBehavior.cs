using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using ReactiveUI;

namespace WalletWasabi.Fluent.Behaviors
{
	public class CloseWindowCommandBehavior : CommandBasedBehavior<Window>
	{
		private CompositeDisposable Disposables { get; } = new CompositeDisposable();
		private IDisposable CommandSubscription { get; set; }

		protected override void OnAttached()
		{
			base.OnAttached();

			this.GetObservable(CommandProperty)
				.Where(cmd => cmd is { })
				.Subscribe(cmd =>
				{
					CommandSubscription?.Dispose();

					CommandSubscription = ObservableExtensions.Subscribe(
						(ReactiveCommand<Unit, Unit>)cmd,
						_ => AssociatedObject.Close());
				})
				.DisposeWith(Disposables);
		}

		protected override void OnDetaching()
		{
			CommandSubscription?.Dispose();
			Disposables?.Dispose();
			base.OnDetaching();
		}
	}
}