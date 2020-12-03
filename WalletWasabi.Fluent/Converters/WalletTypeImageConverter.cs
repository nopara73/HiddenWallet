using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Globalization;
using WalletWasabi.Fluent.ViewModels.AddWallet;

namespace WalletWasabi.Fluent.Converters
{
	public class WalletTypeImageConverter : IValueConverter
	{
		public static readonly WalletTypeImageConverter Instance = new WalletTypeImageConverter();

		private WalletTypeImageConverter()
		{
		}

		object? IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is WalletType type)
			{
				Uri uri = new("avares://WalletWasabi.Fluent/Assets/HardwareIcons/generic.png");

				switch (type)
				{
					case WalletType.Coldcard:					
						uri = new("avares://WalletWasabi.Fluent/Assets/HardwareIcons/coldcard.png");						
						break;

					case WalletType.Trezor:
						uri = new("avares://WalletWasabi.Fluent/Assets/HardwareIcons/trezor.png");
						break;

					case WalletType.Ledger:
						uri = new("avares://WalletWasabi.Fluent/Assets/HardwareIcons/ledger.png");
						break;

					case WalletType.Normal:
						uri = new("avares://WalletWasabi.Fluent/Assets/HardwareIcons/normal.png");
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
