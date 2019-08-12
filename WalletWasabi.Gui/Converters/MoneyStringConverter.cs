using Avalonia;
using Avalonia.Data.Converters;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WalletWasabi.Gui.Converters
{
	public class MoneyStringConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is null)
			{
				return "0";
			}
			else if (value is Money money)
			{
				var uiConfig = Application.Current.Resources[Global.UiConfigResourceKey] as UiConfig;

				return uiConfig.SatsDenominated ? money.ToFormattedSatsString() : money.ToString(fplus: false, trimExcessZero: true);
			}
			else
			{
				throw new TypeArgumentException(value, typeof(Money), nameof(value));
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
