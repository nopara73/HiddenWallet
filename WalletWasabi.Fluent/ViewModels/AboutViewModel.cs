using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Dialogs;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Helpers;
using WalletWasabi.Logging;

namespace WalletWasabi.Fluent.ViewModels
{
	[NavigationMetaData(
		Title = "About Wasabi",
		Caption = "Displays all the current info about the app",
		IconName = "info_regular",
		Order = 4,
		Category = "General",
		Keywords = new[] { "About", "Software", "Version", "Source", "Code", "Github", "Status", "Stats", "Tor", "Onion", "Bug", "Report", "FAQ", "Questions,", "Docs", "Documentation", "Link", "Links", "Help" },
		NavBarPosition = NavBarPosition.None)]
	public partial class AboutViewModel : RoutableViewModel
	{
		public AboutViewModel()
		{
			OpenBrowserCommand = ReactiveCommand.CreateFromTask<string>(IoHelpers.OpenBrowserAsync);

			var interaction = new Interaction<Unit, Unit>();
			interaction.RegisterHandler(
				async x =>
					x.SetOutput(await new AboutAdvancedInfoViewModel().ShowDialogAsync()));

			AboutAdvancedInfoDialogCommand = ReactiveCommand.CreateFromTask(
				execute: async () => await interaction.Handle(Unit.Default).ToTask());

			OpenBrowserCommand.ThrownExceptions
				.ObserveOn(RxApp.TaskpoolScheduler)
				.Subscribe(ex => Logger.LogError(ex));
		}

		public override NavigationTarget DefaultTarget => NavigationTarget.DialogScreen;

		public ICommand AboutAdvancedInfoDialogCommand { get; }

		public ReactiveCommand<string, Unit> OpenBrowserCommand { get; }

		public Version ClientVersion => Constants.ClientVersion;

		public string ClearnetLink => "https://wasabiwallet.io/";

		public string TorLink => "http://wasabiukrxmkdgve5kynjztuovbg43uxcbcxn6y2okcrsg7gb6jdmbad.onion";

		public string SourceCodeLink => "https://github.com/zkSNACKs/WalletWasabi/";

		public string StatusPageLink => "https://stats.uptimerobot.com/YQqGyUL8A7";

		public string UserSupportLink => "https://www.reddit.com/r/WasabiWallet/";

		public string BugReportLink => "https://github.com/zkSNACKs/WalletWasabi/issues/";

		public string FAQLink => "https://docs.wasabiwallet.io/FAQ/";

		public string DocsLink => "https://docs.wasabiwallet.io/";

		public string License => "https://github.com/zkSNACKs/WalletWasabi/blob/master/LICENSE.md";
	}
}
