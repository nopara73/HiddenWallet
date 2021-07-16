using Avalonia;
using Avalonia.Controls;
using Avalonia.Dialogs;
using Avalonia.ReactiveUI;
using Splat;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Fluent.CrashReport;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.ViewModels;
using WalletWasabi.Gui;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;
using WalletWasabi.Services;
using WalletWasabi.Services.Terminate;
using WalletWasabi.Wallets;
using LogLevel = WalletWasabi.Logging.LogLevel;

namespace WalletWasabi.Fluent.Desktop
{
	public class Program
	{
		private static Global? Global;

		private static readonly TerminateService TerminateService = new(TerminateApplicationAsync);

		// Initialization code. Don't use any Avalonia, third-party APIs or any
		// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
		// yet and stuff might break.
		public static int Main(string[] args)
		{
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
			TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
			bool runGui = true;

			try
			{
				if (CrashReporter.TryGetExceptionFromCliArgs(args, out var exceptionToShow))
				{
					// Show the exception.
					Console.WriteLine($"TODO Implement crash reporting. {exceptionToShow}");

					runGui = false;
				}
			}
			catch (Exception ex)
			{
				// Anything happens here just log it and do not run the Gui.
				Logger.LogCritical(ex);
				runGui = false;
			}

			Exception? exceptionToReport = null;
			SingleInstanceChecker? singleInstanceChecker = null;

			if (runGui)
			{
				try
				{
					string dataDir = EnvironmentHelpers.GetDataDir(Path.Combine("WalletWasabi", "Client"));

					SetupLogger(dataDir, args);
					var (uiConfig, config) = LoadOrCreateConfigs(dataDir);

					singleInstanceChecker = new SingleInstanceChecker(config.Network);
					singleInstanceChecker.EnsureSingleOrThrowAsync().GetAwaiter().GetResult();

					Global = CreateGlobal(dataDir, uiConfig, config);

					// TODO only required due to statusbar vm... to be removed.
					Locator.CurrentMutable.RegisterConstant(Global);

					Services.Initialize(Global);

					Logger.LogSoftwareStarted("Wasabi GUI");
					BuildAvaloniaApp()
						.AfterSetup(_ => ThemeHelper.ApplyTheme(Global.UiConfig.DarkModeEnabled ? Theme.Dark : Theme.Light))
						.StartWithClassicDesktopLifetime(args);
				}
				catch (OperationCanceledException ex)
				{
					Logger.LogDebug(ex);
				}
				catch (Exception ex)
				{
					exceptionToReport = ex;
					Logger.LogCritical(ex);
				}
			}

			// Start termination/disposal of the application.

			TerminateService.Terminate();

			if (singleInstanceChecker is { } single)
			{
				Task.Run(async () => await single.DisposeAsync()).Wait();
			}

			if (exceptionToReport is { })
			{
				// Trigger the CrashReport process if required.
				CrashReporter.Invoke(exceptionToReport);
			}

			AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
			TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;

			Logger.LogSoftwareStopped("Wasabi");

			return exceptionToReport is { } ? 1 : 0;
		}

		/// <summary>
		/// Initializes Wasabi Logger. Sets user-defined log-level, if provided.
		/// </summary>
		/// <example>Start Wasabi Wallet with <c>./wassabee --LogLevel=trace</c> to set <see cref="LogLevel.Trace"/>.</example>
		private static void SetupLogger(string dataDir, string[] args)
		{
			LogLevel? logLevel = null;

			foreach (string arg in args)
			{
				if (arg.StartsWith("--LogLevel="))
				{
					string value = arg.Split('=', count: 2)[1];

					if (Enum.TryParse(value, ignoreCase: true, out LogLevel parsedLevel))
					{
						logLevel = parsedLevel;
						break;
					}
				}
			}

			Logger.InitializeDefaults(Path.Combine(dataDir, "Logs.txt"), logLevel);
		}

		private static (UiConfig uiConfig, Config config) LoadOrCreateConfigs(string dataDir)
		{
			Directory.CreateDirectory(dataDir);

			UiConfig uiConfig = new(Path.Combine(dataDir, "UiConfig.json"));
			uiConfig.LoadOrCreateDefaultFile();

			Config config = new(Path.Combine(dataDir, "Config.json"));
			config.LoadOrCreateDefaultFile();
			config.CorrectMixUntilAnonymitySet();

			return (uiConfig, config);
		}

		private static Global CreateGlobal(string dataDir, UiConfig uiConfig, Config config)
		{
			var walletManager = new WalletManager(config.Network, dataDir, new WalletDirectories(config.Network, dataDir));

			return new Global(dataDir, config, uiConfig, walletManager);
		}

		/// <summary>
		/// Do not call this method it should only be called by TerminateService.
		/// </summary>
		private static async Task TerminateApplicationAsync()
		{
			if (MainViewModel.Instance is { } mainViewModel)
			{
				mainViewModel.ClearStacks();
				mainViewModel.StatusBar.Dispose();
				Logger.LogSoftwareStopped("Wasabi GUI");
			}

			if (Global is { } global)
			{
				await global.DisposeAsync().ConfigureAwait(false);
			}
		}

		private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs? e)
		{
			if (e?.Exception is Exception ex)
			{
				Logger.LogWarning(ex);
			}
		}

		private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs? e)
		{
			if (e?.ExceptionObject is Exception ex)
			{
				Logger.LogWarning(ex);
			}
		}

		// Avalonia configuration, don't remove; also used by visual designer.
		private static AppBuilder BuildAvaloniaApp()
		{
			bool useGpuLinux = true;

			var result = AppBuilder.Configure(() => new App(async () =>
				{
					if (Global is { } global)
					{
						await global.InitializeNoWalletAsync(TerminateService);
					}
				}))
				.UseReactiveUI();

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				result
					.UseWin32()
					.UseSkia();
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				result.UsePlatformDetect()
					.UseManagedSystemDialogs<AppBuilder, Window>();
			}
			else
			{
				result.UsePlatformDetect();
			}

			return result
				.With(new Win32PlatformOptions { AllowEglInitialization = true, UseDeferredRendering = true, UseWindowsUIComposition = true })
				.With(new X11PlatformOptions { UseGpu = useGpuLinux, WmClass = "Wasabi Wallet" })
				.With(new AvaloniaNativePlatformOptions { UseDeferredRendering = true, UseGpu = true })
				.With(new MacOSPlatformOptions { ShowInDock = true });
		}
	}
}
