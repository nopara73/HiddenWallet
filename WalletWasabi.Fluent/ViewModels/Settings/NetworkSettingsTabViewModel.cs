using System;
using System.Net;
using System.Reactive.Linq;
using ReactiveUI;
using WalletWasabi.Gui;
using WalletWasabi.Gui.Validation;
using WalletWasabi.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Userfacing;

namespace WalletWasabi.Fluent.ViewModels.Settings
{
	[NavigationMetaData(
		Title = "Network",
		Caption = "Manage network settings",
		Order = 2,
		Category = "Settings",
		Keywords = new[]
		{
			"Settings", "Network", "Encryption", "Tor", "Terminate", "Wasabi", "Shutdown", "SOCKS5", "Endpoint"
		},
		IconName = "settings_network_regular")]
	public partial class NetworkSettingsTabViewModel : SettingsTabViewModelBase
	{
		[AutoNotify] private bool _useTor;
		[AutoNotify] private bool _terminateTorOnExit;
		[AutoNotify] private string _torSocks5EndPoint;

		public NetworkSettingsTabViewModel(Config config, UiConfig uiConfig) : base(config, uiConfig)
		{
			this.ValidateProperty(x => x.TorSocks5EndPoint, ValidateTorSocks5EndPoint);

			_useTor = config.UseTor;
			_terminateTorOnExit = config.TerminateTorOnExit;
			_torSocks5EndPoint = config.TorSocks5EndPoint.ToString(-1);

			this.WhenAnyValue(
					x => x.UseTor,
					x => x.UseTor,
					x => x.TorSocks5EndPoint)
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Throttle(TimeSpan.FromMilliseconds(ThrottleTime))
				.Skip(1)
				.Subscribe(_ => Save());
		}

		private void ValidateTorSocks5EndPoint(IValidationErrors errors)
			=> ValidateEndPoint(errors, TorSocks5EndPoint, Constants.DefaultTorSocksPort, whiteSpaceOk: true);

		private void ValidateEndPoint(IValidationErrors errors, string endPoint, int defaultPort, bool whiteSpaceOk)
		{
			if (!whiteSpaceOk || !string.IsNullOrWhiteSpace(endPoint))
			{
				if (!EndPointParser.TryParse(endPoint, defaultPort, out _))
				{
					errors.Add(ErrorSeverity.Error, "Invalid endpoint.");
				}
			}
		}

		protected override void EditConfigOnSave(Config config)
		{
			if (EndPointParser.TryParse(TorSocks5EndPoint, Constants.DefaultTorSocksPort, out EndPoint torEp))
			{
				config.TorSocks5EndPoint = torEp;
			}

			config.UseTor = UseTor;
			config.TerminateTorOnExit = TerminateTorOnExit;
		}
	}
}