using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using WalletWasabi.Fluent.ViewModels.NavBar;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace WalletWasabi.Fluent.ViewModels.Search
{
	[NavigationMetaData(
		Title = "Search",
		IconName = "search_regular",
		Searchable = false,
		NavBarPosition = NavBarPosition.Bottom)]
	public partial class SearchPageViewModel : NavBarItemViewModel
	{
		private readonly Dictionary<string, SearchCategory> _categories;
		private readonly Dictionary<SearchCategory, SourceList<SearchItemViewModel>> _categorySources;
		private ReadOnlyObservableCollection<SearchResult>? _searchResults;
		private IObservable<IChangeSet<SearchItemViewModel>>? _sourceObservable;
		[AutoNotify] private string? _searchQuery;

		public SearchPageViewModel()
		{
			Title = "Search";
			_categories = new Dictionary<string, SearchCategory>();
			_categorySources = new Dictionary<SearchCategory, SourceList<SearchItemViewModel>>();
		}

		public ReadOnlyObservableCollection<SearchResult> SearchResults => _searchResults!;

		public void Initialise()
		{
			foreach (var metaData in NavigationManager.MetaData.Where(
				x => x.Searchable))
			{
				RegisterSearchEntry(metaData);
			}

			var queryFilter = this.WhenValueChanged(t => t.SearchQuery)
				.Throttle(TimeSpan.FromMilliseconds(100))
				.Select(SearchQueryFilter)
				.DistinctUntilChanged();

			_sourceObservable
				.Filter(queryFilter)
				.GroupWithImmutableState(x => x.Category)
				.Transform(grouping => new SearchResult(grouping.Key, grouping.Items.OrderBy(x => x.Order).ThenBy(x => x.Title)))
				.Sort(SortExpressionComparer<SearchResult>.Ascending(i => i.Category.Order).ThenByAscending(i => i.Category.Title))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Bind(out _searchResults)
				.AsObservableList();
		}

		public void RegisterCategory(string title, int order)
		{
			if (!_categories.ContainsKey(title))
			{
				var category = new SearchCategory(title, order);

				_categories.Add(title, category);

				var sourceList = new SourceList<SearchItemViewModel>();

				_categorySources.Add(category, sourceList);

				if (_sourceObservable is null)
				{
					_sourceObservable = sourceList.Connect();
				}
				else
				{
					_sourceObservable = _sourceObservable.Merge(sourceList.Connect());
				}
				return;
			}

			throw new Exception("Category already exists.");
		}

		private void RegisterSearchEntry(NavigationMetaData metaData)
		{
			if (_categories.TryGetValue(metaData.Category, out var searchCategory))
			{
				var result = new SearchItemViewModel(metaData, searchCategory);

				_categorySources[searchCategory].Add(result);

				return;
			}

			throw new Exception("Category doesn't exist.");
		}

		private Func<SearchItemViewModel, bool> SearchQueryFilter(string? searchQuery)
		{
			return item =>
			{
				if (!string.IsNullOrWhiteSpace(searchQuery)
				    && !searchQuery.Contains(',', StringComparison.OrdinalIgnoreCase))
				{
					return item.Keywords.Any(x => x.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) ||
					       item.Caption.Contains(searchQuery, StringComparison.OrdinalIgnoreCase);
				}
				return true;
			};
		}
	}
}
