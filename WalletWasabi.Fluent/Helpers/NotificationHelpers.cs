using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Concurrency;
using Avalonia.Controls.Notifications;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.Helpers
{
	public static class NotificationHelpers
	{
		private const int DefaultNotificationTimeout = 7;
		private static WindowNotificationManager? NotificationManager;

		public static void SetNotificationManager(WindowNotificationManager windowNotificationManager)
		{
			if (NotificationManager is { })
			{
				throw new InvalidOperationException("Already set!");
			}

			NotificationManager = windowNotificationManager;
		}

		public static void Show(string title, string message)
		{
			if (NotificationManager is { } nm)
			{
				RxApp.MainThreadScheduler.Schedule(() => nm.Show(new Notification(title, message, NotificationType.Information, TimeSpan.FromSeconds(DefaultNotificationTimeout))));
			}
		}

		public static void Show(ProcessedResult result)
		{
			if (TryGetNotificationInputs(result, out var title, out var message))
			{
				Show(title, message);
			}
		}

		private static bool TryGetNotificationInputs(ProcessedResult result, [NotNullWhen(true)] out string? title, [NotNullWhen(true)] out string? message)
		{
			title = null;
			message = null;

			try
			{
				bool isSpent = result.NewlySpentCoins.Any();
				bool isReceived = result.NewlyReceivedCoins.Any();
				bool isConfirmedReceive = result.NewlyConfirmedReceivedCoins.Any();
				bool isConfirmedSpent = result.NewlyConfirmedReceivedCoins.Any();
				Money miningFee = result.Transaction.Transaction.GetFee(result.SpentCoins.Select(x => x.Coin).ToArray());

				if (isReceived || isSpent)
				{
					Money receivedSum = result.NewlyReceivedCoins.Sum(x => x.Amount);
					Money spentSum = result.NewlySpentCoins.Sum(x => x.Amount);
					Money incoming = receivedSum - spentSum;
					Money receiveSpentDiff = incoming.Abs();
					string amountString = receiveSpentDiff.ToFormattedString();
					message = $"{amountString} BTC";

					if (result.Transaction.Transaction.IsCoinBase)
					{
						title = "Mined";
					}
					else if (isSpent && receiveSpentDiff == miningFee)
					{
						title = "Self Spend";
						message = $"Mining Fee: {amountString} BTC";
					}
					else if (incoming > Money.Zero)
					{
						title = "Transaction Received";
					}
					else if (incoming < Money.Zero)
					{
						title = "Transaction Sent";
					}
				}
				else if (isConfirmedReceive || isConfirmedSpent)
				{
					Money receivedSum = result.ReceivedCoins.Sum(x => x.Amount);
					Money spentSum = result.SpentCoins.Sum(x => x.Amount);
					Money incoming = receivedSum - spentSum;
					Money receiveSpentDiff = incoming.Abs();
					string amountString = receiveSpentDiff.ToFormattedString();
					message = $"{amountString} BTC";

					if (isConfirmedSpent && receiveSpentDiff == miningFee)
					{
						title = "Self Spend Confirmed";
						message = $"Mining Fee: {amountString} BTC";
					}
					else if (incoming > Money.Zero)
					{
						title = "Receive Confirmed";
					}
					else if (incoming < Money.Zero)
					{
						title = "Send Confirmed";
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex);
			}

			return title is { } && message is { };
		}
	}
}