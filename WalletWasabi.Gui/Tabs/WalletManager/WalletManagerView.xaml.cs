using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvalonStudio.Extensibility;
using AvalonStudio.Shell;
using System;
using System.IO;
using System.Linq;

namespace WalletWasabi.Gui.Tabs.WalletManager
{
	internal class WalletManagerView : UserControl
	{
		public WalletManagerView()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}
	}
}
