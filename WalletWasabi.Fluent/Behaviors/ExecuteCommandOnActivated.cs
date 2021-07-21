using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace WalletWasabi.Fluent.Behaviors
{
	public class ExecuteCommandOnActivated : DisposingBehavior<Control>
	{
		public static readonly StyledProperty<ICommand?> CommandProperty =
			AvaloniaProperty.Register<ExecuteCommandOnActivated, ICommand?>(nameof(Command));

		public ICommand? Command
		{
			get => GetValue(CommandProperty);
			set => SetValue(CommandProperty, value);
		}

		protected override void OnAttached(CompositeDisposable disposables)
		{
			var mainWindow = ((IClassicDesktopStyleApplicationLifetime) Application.Current.ApplicationLifetime).MainWindow;

			Observable
				.FromEventPattern(mainWindow, nameof(mainWindow.Activated))
				.Subscribe(_ =>
				{
					if (Command is { } cmd && cmd.CanExecute(default))
					{
						cmd.Execute(default);
					}
				})
				.DisposeWith(disposables);
		}
	}
}
