using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Globalization;

namespace WalletWasabi.Fluent.Converters
{
	public class HardwareWalletIconConverter : IValueConverter
	{
		public static readonly HardwareWalletIconConverter Instance = new HardwareWalletIconConverter();

		private HardwareWalletIconConverter()
		{
		}

		object? IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is Gui.Tabs.WalletManager.HardwareWallets.HardwareWalletViewModel hwwvm)
			{
				Uri uri = new ("avares://WalletWasabi.Fluent/Assets/HardwareIcons/generic.png");

				switch (hwwvm.HardwareWalletInfo.Model)
				{
					case Hwi.Models.HardwareWalletModels.Coldcard:
					case Hwi.Models.HardwareWalletModels.Coldcard_Simulator:
						uri = new ("avares://WalletWasabi.Fluent/Assets/HardwareIcons/coldcard.png");						
						break;

					case Hwi.Models.HardwareWalletModels.Trezor_1:
					case Hwi.Models.HardwareWalletModels.Trezor_1_Simulator:
					case Hwi.Models.HardwareWalletModels.Trezor_T:
					case Hwi.Models.HardwareWalletModels.Trezor_T_Simulator:
						uri = new("avares://WalletWasabi.Fluent/Assets/HardwareIcons/trezor.png");
						break;

					case Hwi.Models.HardwareWalletModels.Ledger_Nano_S:
						uri = new("avares://WalletWasabi.Fluent/Assets/HardwareIcons/ledger.png");
						break;						
				}

				var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();

				using var image = assets.Open(uri);
				return new Bitmap(image);
			}

			return null;
		}

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
