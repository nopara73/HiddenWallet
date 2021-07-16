using System;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using WalletWasabi.Blockchain.Blocks;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets
{
	public partial class LoadingViewModel : ActivatableViewModel
	{
		private readonly Wallet _wallet;

		[AutoNotify] private double _percent;
		[AutoNotify] private string? _statusText;

		private Stopwatch? _stopwatch;
		private bool _isLoading;
		private uint _filtersToDownloadCount;
		private uint _filtersToProcessCount;
		private uint _filterProcessStartingHeight;

		public LoadingViewModel(Wallet wallet)
		{
			_wallet = wallet;
			_statusText = "";
			_percent = 0;
		}

		private uint TotalCount => _filtersToProcessCount + _filtersToDownloadCount;

		private uint RemainingFiltersToDownload => (uint) Services.BitcoinStore.SmartHeaderChain.HashesLeft;

		protected override void OnActivated(CompositeDisposable disposables)
		{
			base.OnActivated(disposables);

			Percent = 0;
			StatusText = "";
			_stopwatch ??= Stopwatch.StartNew();

			Observable.Interval(TimeSpan.FromSeconds(1))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ =>
				{
					var processedCount = GetCurrentProcessedCount();
					UpdateStatus(processedCount);
				})
				.DisposeWith(disposables);

			if (!_isLoading)
			{
				Services.Synchronizer.WhenAnyValue(x => x.BackendStatus)
					.Where(status => status == BackendStatus.Connected)
					.Subscribe(async _ => await LoadWalletAsync(isBackendAvailable: true).ConfigureAwait(false))
					.DisposeWith(disposables);

				Observable.FromEventPattern<bool>(Services.Synchronizer, nameof(Services.Synchronizer.ResponseArrivedIsGenSocksServFail))
					.Subscribe(async _ =>
					{
						if (Services.Synchronizer.BackendStatus == BackendStatus.Connected)
						{
							return;
						}

						await LoadWalletAsync(isBackendAvailable: false).ConfigureAwait(false);
					})
					.DisposeWith(disposables);
			}
		}

		private uint GetCurrentProcessedCount()
		{
			uint downloadedFilters = 0;
			if (_filtersToDownloadCount > 0)
			{
				downloadedFilters = _filtersToDownloadCount - RemainingFiltersToDownload;
			}

			uint processedFilters = 0;
			if (_wallet.LastProcessedFilter?.Header?.Height is { } lastProcessedFilterHeight)
			{
				processedFilters = lastProcessedFilterHeight - _filterProcessStartingHeight - 1;
			}

			var processedCount = downloadedFilters + processedFilters;

			Console.WriteLine($"Total: {TotalCount}/{processedCount} Downloaded: {_filtersToDownloadCount}/{downloadedFilters} Processed: {_filtersToProcessCount}/{processedFilters}");

			return processedCount;
		}

		private async Task LoadWalletAsync(bool isBackendAvailable)
		{
			if (_isLoading)
			{
				return;
			}

			_isLoading = true;

			await SetInitValuesAsync(isBackendAvailable).ConfigureAwait(false);

			while (isBackendAvailable && RemainingFiltersToDownload > 0)
			{
				await Task.Delay(1000).ConfigureAwait(false);
			}

			await UiServices.WalletManager.LoadWalletAsync(_wallet).ConfigureAwait(false);
		}

		private async Task SetInitValuesAsync(bool isBackendAvailable)
		{
			while (isBackendAvailable && Services.Synchronizer.LastResponse is null)
			{
				await Task.Delay(500).ConfigureAwait(false);
			}

			_filtersToDownloadCount = (uint) Services.BitcoinStore.SmartHeaderChain.HashesLeft;

			if (Services.BitcoinStore.SmartHeaderChain.ServerTipHeight is { } serverTipHeight &&
			    Services.BitcoinStore.SmartHeaderChain.TipHeight is { } clientTipHeight)
			{
				var tipHeight = Math.Max(serverTipHeight, clientTipHeight);
				var startingHeight = SmartHeader.GetStartingHeader(_wallet.Network).Height;
				var bestHeight = (uint) _wallet.KeyManager.GetBestHeight().Value;
				_filterProcessStartingHeight = bestHeight < startingHeight ? startingHeight : bestHeight;

				_filtersToProcessCount = tipHeight - _filterProcessStartingHeight;
			}
		}

		private void UpdateStatus(uint processedCount)
		{
			if (TotalCount == 0 || processedCount == 0 || processedCount > TotalCount || _stopwatch is null)
			{
				return;
			}

			var percent = (decimal) processedCount / TotalCount * 100;
			var remainingCount = TotalCount - processedCount;
			var tempPercent = (uint) Math.Round(percent);

			if (tempPercent == 0)
			{
				return;
			}

			Percent = tempPercent;
			var percentText = $"{Percent}% completed";

			var remainingMilliseconds = (double) _stopwatch.ElapsedMilliseconds / processedCount * remainingCount;
			var userFriendlyTime = TextHelpers.TimeSpanToFriendlyString(TimeSpan.FromMilliseconds(remainingMilliseconds));
			var remainingTimeText = string.IsNullOrEmpty(userFriendlyTime) ? "" : $"- {userFriendlyTime} remaining";

			StatusText = $"{percentText} {remainingTimeText}";
		}
	}
}
