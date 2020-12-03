using System;
using System.Text;
using WalletWasabi.Extensions;
using WalletWasabi.Hwi.Models;

namespace WalletWasabi.Gui.Tabs.WalletManager.HardwareWallets
{
	public class HardwareWalletViewModel
	{
		public HardwareWalletViewModel(HwiEnumerateEntry hwi)
		{
			string typeString = hwi.Model.FriendlyName();
			var walletNameBuilder = new StringBuilder(typeString);

			if (hwi.NeedsPinSent is true)
			{
				walletNameBuilder.Append(" - Needs PIN Sent");
			}
			else if (hwi.NeedsPassphraseSent is true)
			{
				walletNameBuilder.Append(" - Needs Passphrase Sent");
			}
			else if (!string.IsNullOrWhiteSpace(hwi.Error))
			{
				walletNameBuilder.Append($" - Error: {hwi.Error}");
			}
			else if (hwi.Code is { })
			{
				walletNameBuilder.Append($" - Error: {hwi.Code}");
			}
			else if (hwi.Fingerprint is null)
			{
				walletNameBuilder.Append(" - Could Not Acquire Fingerprint");
			}
			else if (!hwi.IsInitialized())
			{
				walletNameBuilder.Append(" - Not initialized.");
			}

			WalletName = walletNameBuilder.ToString();
			HardwareWalletInfo = hwi;
		}

		public string WalletName { get; }
		public HwiEnumerateEntry HardwareWalletInfo { get; }

		public override string ToString()
		{
			return WalletName;
		}

		public override bool Equals(object? obj)
		{
			return obj is HardwareWalletViewModel otherWallet &&
			       otherWallet.HardwareWalletInfo.Model == HardwareWalletInfo.Model &&
			       otherWallet.HardwareWalletInfo.Path == HardwareWalletInfo.Path &&
			       otherWallet.HardwareWalletInfo.Fingerprint == HardwareWalletInfo.Fingerprint;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(HardwareWalletInfo.Model, HardwareWalletInfo.Path, HardwareWalletInfo.Fingerprint);
		}
	}
}
