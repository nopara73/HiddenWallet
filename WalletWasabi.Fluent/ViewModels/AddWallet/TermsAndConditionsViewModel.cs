using System.IO;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Legal;

namespace WalletWasabi.Fluent.ViewModels.AddWallet
{
	public partial class TermsAndConditionsViewModel : RoutableViewModel
	{
		[AutoNotify] private bool _isAgreed;

		public TermsAndConditionsViewModel(LegalDocuments legalDocuments, RoutableViewModel next)
		{
			ViewTermsCommand = ReactiveCommand.CreateFromTask(
				async () =>
				{
					var content = await File.ReadAllTextAsync(legalDocuments.FilePath);

					var legalDocs = new LegalDocumentsViewModel(content);

					Navigate().To(legalDocs);
				});

			NextCommand = ReactiveCommand.Create(
				() =>
				{
					Navigate().BackTo(next);
				},
				this.WhenAnyValue(x => x.IsAgreed).ObserveOn(RxApp.MainThreadScheduler));
		}

		public ICommand ViewTermsCommand { get; }
	}
}