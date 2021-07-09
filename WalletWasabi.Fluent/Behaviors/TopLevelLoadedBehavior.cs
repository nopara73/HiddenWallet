using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WalletWasabi.Fluent.Behaviors
{
	public class TopLevelLoadedBehavior : AttachedToVisualTreeBehavior<Visual>, IRenderLoopTask
	{
		private bool _triggered;

		private IDisposable? _disposable;

		public static readonly StyledProperty<bool> IsLoadedProperty =
			AvaloniaProperty.Register<TopLevelLoadedBehavior, bool>(nameof(IsLoaded), defaultBindingMode: BindingMode.TwoWay);

		public bool NeedsUpdate { get; }

		public bool IsLoaded
		{
			get => GetValue(IsLoadedProperty);
			set => SetValue(IsLoadedProperty, value);
		}

		protected override void OnAttachedToVisualTree()
		{
			if (AssociatedObject.GetVisualRoot() is TopLevel topLevel)
			{
				var loop = AvaloniaLocator.Current.GetService<IRenderLoop>();

				_disposable = Disposable.Create(() => loop.Remove(this));

				loop.Add(this);
			}
		}

		protected override void OnDetaching()
		{
			base.OnDetaching();
			_disposable?.Dispose();
		}

		public void Update(TimeSpan time)
		{
			// throw new NotImplementedException();
		}

		public void Render()
		{
			if (!_triggered)
			{
				_triggered = true;

				Dispatcher.UIThread.Post(async () =>
				{
					_disposable?.Dispose();

					await Task.Delay(750);
					IsLoaded = true;
				});
			}
		}
	}
}
