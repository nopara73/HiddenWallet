using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using Splat;
using WalletWasabi.Gui.Helpers;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.CrashReport.ViewModels
{
	public class CrashReportWindowViewModel : ViewModelBase
	{
		public CrashReportWindowViewModel(CrashReporter crashReporter)
		{
			CrashReporter = crashReporter;

			OpenLogCommand = ReactiveCommand.CreateFromTask(async () => await FileHelpers.OpenFileInTextEditorAsync(Logger.FilePath));

			ExitCommand = ReactiveCommand.Create(() =>
			{

			});

			Observable
				.Merge(OpenLogCommand.ThrownExceptions)
				.Merge(ExitCommand.ThrownExceptions)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(ex => Logger.LogError(ex));
		}

		private CrashReporter CrashReporter { get; }
		public string Details => $"Unfortunately, Wasabi has crashed. For more information, please open the log file. You may report this crash to the support team.{Environment.NewLine}{Environment.NewLine}Please always consider your privacy before sharing any information!{Environment.NewLine}{Environment.NewLine}Exception information:";
		public string Message => $"{CrashReporter?.SerializedException?.Message}{Environment.NewLine}{CrashReporter?.SerializedException?.StackTrace ?? ""}";
		public string Title =>  "Warning!";

		public ReactiveCommand<Unit, Unit> OpenLogCommand { get; }
		public ReactiveCommand<Unit, Unit> ExitCommand { get; }
		public ICommand NextCommand => ExitCommand;
		public ICommand CancelCommand => OpenLogCommand;
	}
}
