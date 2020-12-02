using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WalletWasabi.Fluent.CrashReport.ViewModels;
using CrashReportWindow = WalletWasabi.Fluent.CrashReport.Views.CrashReportWindow;

namespace WalletWasabi.Fluent.CrashReport
{
	public class CrashReportApp : Application
	{
		public CrashReportApp()
		{
			Name = "WasabiWallet Crash Report";
		}

		public override void Initialize()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public override void OnFrameworkInitializationCompleted()
		{
			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.MainWindow = new CrashReportWindow()
				{
					DataContext = new CrashReportWindowViewModel()
				};
			}

			base.OnFrameworkInitializationCompleted();
		}
	}
}
