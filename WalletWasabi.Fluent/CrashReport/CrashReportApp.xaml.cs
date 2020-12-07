using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WalletWasabi.Fluent.CrashReport.ViewModels;
using CrashReportWindow = WalletWasabi.Fluent.CrashReport.Views.CrashReportWindow;
using CrashReporter = WalletWasabi.Fluent.CrashReport.CrashReporter;

namespace WalletWasabi.Fluent.CrashReport
{
	public class CrashReportApp : Application
	{
		private readonly CrashReporter _crashReporter;

		public CrashReportApp()
		{
			Name = "Wasabi Wallet Crash Report";
		}

		public CrashReportApp(CrashReporter crashReporter) : this()
		{
			_crashReporter = crashReporter;
		}

		public override void Initialize()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.MainWindow = new CrashReportWindow
				{
					DataContext = new CrashReportWindowViewModel(_crashReporter)
				};
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}
