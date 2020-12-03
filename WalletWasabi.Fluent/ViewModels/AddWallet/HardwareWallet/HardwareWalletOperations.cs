using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Hwi;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Logging;
using WalletWasabi.Wallets;
using Timer = System.Timers.Timer;

namespace WalletWasabi.Fluent.ViewModels.AddWallet.HardwareWallet
{
	public class HardwareWalletOperations : IDisposable
	{
		public event EventHandler<HwiEnumerateEntry[]>? HardwareWalletsFound;
		public event EventHandler? NoHardwareWalletFound;

		public HardwareWalletOperations(WalletManager walletManager, Network network)
		{
			WalletManager = walletManager;
			Network = network;
			Client = new HwiClient(network);
			DisposeCts = new CancellationTokenSource();
			Stopwatch = new Stopwatch();

			PassphraseTimer = new Timer(8000) {AutoReset = false};

			StartDetection();
		}

		public HwiEnumerateEntry? SelectedDevice { get; set; }

		public WalletManager WalletManager { get; }

		public Network Network { get; }

		public HwiClient Client { get; }

		private CancellationTokenSource? DetectionCts { get; set; }

		private CancellationTokenSource DisposeCts { get; }

		public Task? DetectionTask { get; set; }

		public Stopwatch Stopwatch { get; }

		public Timer PassphraseTimer { get; }

		private void OnDetectionCompleted(HwiEnumerateEntry[] wallets)
		{
			if (wallets.Length == 0)
			{
				NoHardwareWalletFound?.Invoke(this, EventArgs.Empty);
			}
			else
			{
				HardwareWalletsFound?.Invoke(this,wallets);
			}
		}

		public async Task GenerateWalletAsync(string walletName)
		{
			var selectedDevice = SelectedDevice;

			try
			{
				if (selectedDevice?.Fingerprint is null)
				{
					throw new Exception("Cannot be null.");
				}

				await StopDetectionAsync();

				var fingerPrint = (HDFingerprint) selectedDevice.Fingerprint;
				using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
				var extPubKey = await Client.GetXpubAsync(
					selectedDevice.Model,
					selectedDevice.Path,
					KeyManager.DefaultAccountKeyPath,
					cts.Token).ConfigureAwait(false);
				var path = WalletManager.WalletDirectories.GetWalletFilePaths(walletName).walletFilePath;

				var km = KeyManager.CreateNewHardwareWalletWatchOnly(fingerPrint, extPubKey, path);
				WalletManager.AddWallet(km);
			}
			catch (Exception)
			{
				StartDetection();
				throw;
			}
		}

		public async Task InitHardwareWalletAsync(HwiEnumerateEntry device)
		{
			using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(21));
			using var initCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, DisposeCts.Token);

			// Trezor T doesn't require interactive mode.
			var interactiveMode = !(device.Model == HardwareWalletModels.Trezor_T || device.Model == HardwareWalletModels.Trezor_T_Simulator);

			try
			{
				await Client.SetupAsync(device.Model, device.Path, interactiveMode, initCts.Token);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
			}
		}

		public async Task StopDetectionAsync()
		{
			if (DetectionTask is { } task && DetectionCts is { } cts)
			{
				cts.Cancel();
				await task;
			}
		}

		public void StartDetection()
		{
			if (DisposeCts is { } cts)
			{
				DetectionCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
				DetectionTask = HardwareWalletDetectionAsync(DetectionCts);
			}
		}

		protected async Task HardwareWalletDetectionAsync(CancellationTokenSource detectionCts)
		{
			while (!detectionCts.IsCancellationRequested)
			{
				Stopwatch.Start();

				try
				{
					using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

					PassphraseTimer.Start();
					var detectedHardwareWallets =
						(await Client.EnumerateAsync(timeoutCts.Token)
							.ConfigureAwait(false))
							.Where(wallet => WalletManager.GetWallets()
							.Any(x => x.KeyManager.MasterFingerprint == wallet.Fingerprint) == false)
							.ToArray();

					detectionCts.Token.ThrowIfCancellationRequested();

					OnDetectionCompleted(detectedHardwareWallets);
				}
				catch (Exception ex)
				{
					if (ex is not OperationCanceledException)
					{
						Logger.LogError(ex);
					}
				}
				finally
				{
					PassphraseTimer.Stop();
				}

				// Too fast enumeration causes the detected hardware wallets to be unable to provide the fingerprint.
				// Wait at least 5 seconds between two enumerations.
				Stopwatch.Stop();
				if (Stopwatch.Elapsed.Milliseconds < 5000)
				{
					await Task.Delay(5000 - Stopwatch.Elapsed.Milliseconds).ConfigureAwait(false);
				}
			}
		}

		public void Dispose()
		{
			Task.Run(async () =>
			{
				await StopDetectionAsync();

				DisposeCts.Dispose();
				DetectionCts?.Dispose();
				DetectionTask?.Dispose();
				PassphraseTimer.Dispose();
			});
		}
	}
}
