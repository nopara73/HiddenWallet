namespace WalletWasabi.Fluent.ViewModels.Navigation
{
	public interface INavigationStack<T> where T : INavigatable
	{
		/// <summary>
		/// The Current Page.
		/// </summary>
		T? CurrentPage { get; }

		/// <summary>
		/// True if you can navigate back, else false.
		/// </summary>
		bool CanNavigateBack { get; }

		void To(T viewmodel, NavigationMode mode = NavigationMode.Normal);

		void Back();

		void BackTo(T viewmodel);

		void Clear();
	}
}