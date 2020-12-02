using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.AddWallet
{
	[NavigationMetaData(
		Title = "Legal Docs",
		Caption = "Displays terms and conditions",
		Order = 3,
		Category = "General",
		Keywords = new[] { "View", "Legal", "Docs", "Documentation", "Terms", "Conditions", "Help" },
		IconName = "info_regular")]
	public partial class LegalDocumentsViewModel : RoutableViewModel
	{
		public LegalDocumentsViewModel(string content)
		{
			Content = content;

			NextCommand = BackCommand;
		}

		public override NavigationTarget DefaultTarget => NavigationTarget.DialogScreen;

		public string Content { get; }
	}
}