using System;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Gui.Models.StatusBarStatuses;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Hwi;
using WalletWasabi.Hwi.Exceptions;
using WalletWasabi.Interfaces;

namespace WalletWasabi.Gui.Controls.WalletExplorer
{
	internal class HwiPsbtSigner : IPsbtSigner
	{
		private readonly HwiClient _hwiClient;
		private readonly Action<bool> _hardwareBusyModifier;

		public HwiPsbtSigner(HwiClient hwiClient, Action<bool> hardwareBusyModifier)
		{
			_hwiClient = hwiClient;
			_hardwareBusyModifier = hardwareBusyModifier;
		}

		public async Task<PSBT> TrySign(PSBT psbt, KeyManager keyManager, CancellationToken cancellationToken)
		{
			try
			{
				if (!keyManager.IsHardwareWallet)
				{
					return null;
				}

				_hardwareBusyModifier.Invoke(true);
				MainWindowViewModel.Instance.StatusBar.TryAddStatus(StatusType
					.AcquiringSignatureFromHardwareWallet);
				try
				{
					return await _hwiClient.SignTxAsync(keyManager.MasterFingerprint.Value, psbt, false,
						cancellationToken);
				}
				catch (HwiException)
				{
					await PinPadViewModel.UnlockAsync();
					return await _hwiClient.SignTxAsync(keyManager.MasterFingerprint.Value, psbt, false,
						cancellationToken);
				}
			}
			catch
			{
				return null;
			}
			finally
			{
				MainWindowViewModel.Instance.StatusBar.TryRemoveStatus(StatusType
					.AcquiringSignatureFromHardwareWallet);
				_hardwareBusyModifier.Invoke(false);
			}
		}
	}
}