using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvalonStudio.Documents;
using AvalonStudio.Extensibility;
using AvalonStudio.Shell;
using AvalonStudio.Shell.Controls;
using Splat;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Gui.Controls.WalletExplorer;
using WalletWasabi.Gui.Dialogs;
using WalletWasabi.Gui.Tabs.WalletManager;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Services;

namespace WalletWasabi.Gui
{
	public class MainWindow : MetroWindow
	{
		private long _closingState;

		public MainWindow()
		{
			Global = Locator.Current.GetService<Global>();
			SingleInstanceChecker = Locator.Current.GetService<SingleInstanceChecker>();

			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif

			var notificationManager = new WindowNotificationManager(this)
			{
				Position = NotificationPosition.BottomRight,
				MaxItems = 4,
				Margin = new Thickness(0, 0, 15, 40)
			};

			Locator.CurrentMutable.RegisterConstant<INotificationManager>(notificationManager);

			Closing += MainWindow_ClosingAsync;

			SingleInstanceChecker.OtherInstanceStarted += SingleInstanceChecker_OtherInstanceStarted;
		}

		private Global Global { get; }

		private SingleInstanceChecker SingleInstanceChecker { get; }

		private void SingleInstanceChecker_OtherInstanceStarted(object? sender, EventArgs e)
		{
			Dispatcher.UIThread.PostLogException(() => Show());
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		public void CloseFromMenuAsync()
		{
			switch (Interlocked.CompareExchange(ref _closingState, 1, 0))
			{
				case 0:
					Dispatcher.UIThread.PostLogException(async () => await ClosingAsync());
					break;

				default:
					return;
			}
		}

		private async void MainWindow_ClosingAsync(object? sender, CancelEventArgs e)
		{
			try
			{
				e.Cancel = true;

				if (Interlocked.Read(ref _closingState) == 0)
				{
					if (Global.UiConfig.HideWindowOnClose)
					{
						await HideWindowAsync();
						return;
					}
				}

				switch (Interlocked.CompareExchange(ref _closingState, 1, 0))
				{
					case 0:
						{
							await ClosingAsync();
						}
						break;

					case 1:
						// still closing cancel the progress
						return;

					case 2:
						e.Cancel = false;
						return; //can close the window
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex);
			}
		}

		public async Task HideWindowAsync()
		{
			if (OperatingSystem.IsMacOS())
			{
				
				string shellCommand = $"osascript -e 'tell application \"System Events\" to set visible of process \"Wasabi Wallet\" to false'";
				await EnvironmentHelpers.ShellExecAsync(shellCommand, waitForExit: true).ConfigureAwait(false);
			}
			else
			{
				Hide();
			}
		}

		private async Task ClosingAsync()
		{
			bool closeApplication = false;
			try
			{
				// Indicate -> do not add any more alices to the coinjoin.
				Global.WalletManager.SignalQuitPending(true);

				if (Global.WalletManager.AnyCoinJoinInProgress())
				{
					var dialog = new CannotCloseDialogViewModel();

					closeApplication = await MainWindowViewModel.Instance.ShowDialogAsync(dialog); // start the deque process with a dialog
				}
				else
				{
					closeApplication = true;
				}

				if (closeApplication)
				{
					try
					{
						if (Global.UiConfig is { }) // UiConfig not yet loaded.
						{
							Global.UiConfig.WindowState = WindowState;

							IDocumentTabViewModel? selectedDocument = IoC.Get<IShell>().SelectedDocument;
							Global.UiConfig.LastActiveTab = selectedDocument is null
								? nameof(HistoryTabViewModel)
								: selectedDocument.GetType().Name;

							Global.UiConfig.ToFile();
							Logger.LogInfo($"{nameof(Global.UiConfig)} is saved.");
						}

						Hide();
						var wm = IoC.Get<IShell>().Documents?.OfType<WalletManagerViewModel>().FirstOrDefault();
						if (wm is { })
						{
							wm.OnClose();
							Logger.LogInfo($"{nameof(WalletManagerViewModel)} closed.");
						}
					}
					catch (Exception ex)
					{
						Logger.LogWarning(ex);
					}

					Interlocked.Exchange(ref _closingState, 2); //now we can close the app

					SingleInstanceChecker.OtherInstanceStarted -= SingleInstanceChecker_OtherInstanceStarted;

					Close(); // start the closing process. Will call MainWindow_ClosingAsync again!
				}
				//let's go to finally
			}
			catch (Exception ex)
			{
				Interlocked.Exchange(ref _closingState, 0); //something happened back to starting point
				Logger.LogWarning(ex);
			}
			finally
			{
				if (!closeApplication) //we are not closing the application for some reason
				{
					Interlocked.Exchange(ref _closingState, 0);
					// Re-enable enqueuing coins.
					Global.WalletManager.SignalQuitPending(false);
				}
			}
		}
	}
}
