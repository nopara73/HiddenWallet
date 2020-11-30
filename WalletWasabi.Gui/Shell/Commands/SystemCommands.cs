using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using AvalonStudio.Commands;
using AvalonStudio.Shell;
using ReactiveUI;
using Splat;
using System;
using System.Composition;
using System.Reactive.Linq;
using WalletWasabi.Logging;

namespace WalletWasabi.Gui.Shell.Commands
{
	internal class SystemCommands
	{
		[ImportingConstructor]
		public SystemCommands(CommandIconService commandIconService)
		{
			var global = Locator.Current.GetService<Global>();

			ExitCommand = new CommandDefinition(
				"Exit",
				commandIconService.GetCompletionKindImage("Exit"),
				ReactiveCommand.Create(OnExit));

			LockScreenCommand = new CommandDefinition(
				"Lock Screen",
				commandIconService.GetCompletionKindImage("Lock"),
				ReactiveCommand.Create(() => global.UiConfig.LockScreenActive = true));

			Observable
				.Merge(ExitCommand.GetReactiveCommand().ThrownExceptions)
				.Merge(ExitCommand.GetReactiveCommand().ThrownExceptions)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(ex => Logger.LogWarning(ex));
		}

		[DefaultKeyGesture("ALT+F4")]
		[ExportCommandDefinition("File.Exit")]
		public CommandDefinition ExitCommand { get; }

		[DefaultKeyGesture("CTRL+L", osxKeyGesture: "CMD+L")]
		[ExportCommandDefinition("File.LockScreen")]
		public CommandDefinition LockScreenCommand { get; }

		private void OnExit()
		{
			// This is not called on macOS. On Quit it closes the window directly.
			if (((IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime).MainWindow is MainWindow window)
			{
				window.CloseFromMenuAsync();
			}
		}
	}
}
