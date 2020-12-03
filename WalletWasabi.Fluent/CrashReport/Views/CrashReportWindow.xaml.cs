using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WalletWasabi.Fluent.CrashReport.Views
{
	public class CrashReportWindow : Window
	{
		public CrashReportWindow()
		{
			// Test if the crash reporter window itself crashes.
			throw new Exception("Gremlins in the deck plating.");
			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}
	}
}
