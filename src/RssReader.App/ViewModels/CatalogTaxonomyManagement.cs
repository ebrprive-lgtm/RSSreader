using System.Collections.ObjectModel;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

internal sealed class CatalogTaxonomyManagement : ObservableObject
{
    private readonly Profile _actor;
    private readonly CatalogService _catalogService;
    private readonly Func<ObservableCollection<CatalogCategory>> _categories;
    private readonly Action<string> _setErrorMessage;
    private readonly Func<Task> _refresh;
    private readonly Func<string, Task<bool>> _confirmDelete;
    private string _categoryName = string.Empty;
    private string _collectionName = string.Empty;
    private string? _editingCategoryId;
    private string? _editingCollectionId;

    public CatalogTaxonomyManagement(
        Profile actor,
        CatalogService catalogService,
        Func<ObservableCollection<CatalogCategory>> categories,
        Action<string> setErrorMessage,
        Func<Task> refresh,
        Func<string, Task<bool>> confirmDelete)
    {
        _actor = actor;
        _catalogService = catalogService;
        _categories = categories;
        _setErrorMessage = setErrorMessage;
        _refresh = refresh;
        _confirmDelete = confirmDelete;
    }

    public string CategoryName
    {
        get => _categoryName;
        set
        {
            if (SetProperty(ref _categoryName, value))
            {
                NotifyMergeCategoryProperties();
            }
        }
    }

    public string CollectionName
    {
        get => _collectionName;
        set => SetProperty(ref _collectionName, value);
    }

    public CatalogCategory? MergeTargetCategory =>
        _editingCategoryId is null || string.IsNullOrWhiteSpace(CategoryName)
            ? null
            : _categories().FirstOrDefault(category =>
                category.Id != _editingCategoryId &&
                string.Equals(category.Name, CategoryName.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool CanMergeCategory => MergeTargetCategory is not null;
    public string MergeCategoryButtonLabel => MergeTargetCategory is { } target
        ? $"Merge into {target.Name}"
        : "Merge into existing";

    public void ResetEntryForm()
    {
        _editingCategoryId = null;
        _editingCollectionId = null;
        CategoryName = string.Empty;
        CollectionName = string.Empty;
        NotifyMergeCategoryProperties();
    }

    public void ClearEditingState(bool notifyMergeProperties = false)
    {
        _editingCategoryId = null;
        _editingCollectionId = null;
        if (notifyMergeProperties)
        {
            NotifyMergeCategoryProperties();
        }
    }

    public void PrepareCategoryEdit(CatalogCategory category)
    {
        _editingCategoryId = category.Id;
        _editingCollectionId = null;
        CategoryName = category.Name;
        NotifyMergeCategoryProperties();
    }

    public void PrepareCollectionEdit(CatalogCollection collection)
    {
        _editingCategoryId = null;
        _editingCollectionId = collection.Id;
        CollectionName = collection.Name;
        NotifyMergeCategoryProperties();
    }

    public void NotifyCategoriesChanged() => NotifyMergeCategoryProperties();

    public async Task AddCategoryAsync()
    {
        _setErrorMessage(string.Empty);
        try
        {
            if (_editingCategoryId is { } categoryId)
            {
                await _catalogService.UpdateCategoryAsync(_actor, categoryId, CategoryName);
                _editingCategoryId = null;
            }
            else
            {
                await _catalogService.AddCategoryAsync(_actor, CategoryName);
            }

            CategoryName = string.Empty;
            await _refresh();
        }
        catch (ArgumentException exception)
        {
            _setErrorMessage(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            _setErrorMessage(exception.Message);
        }
    }

    public async Task MergeCategoryIntoExistingAsync()
    {
        _setErrorMessage(string.Empty);
        if (_editingCategoryId is not { } sourceCategoryId ||
            MergeTargetCategory is not { } targetCategory)
        {
            return;
        }

        try
        {
            await _catalogService.MergeCategoriesAsync(_actor, sourceCategoryId, targetCategory.Id);
            _editingCategoryId = null;
            CategoryName = string.Empty;
            await _refresh();
        }
        catch (ArgumentException exception)
        {
            _setErrorMessage(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            _setErrorMessage(exception.Message);
        }
    }

    public async Task DeleteCategoryAsync(CatalogCategory category)
    {
        if (!await _confirmDelete(
                $"Delete category '{category.Name}'? Its feeds remain in the catalog and become uncategorized."))
        {
            return;
        }

        await _catalogService.DeleteCategoryAsync(_actor, category.Id);
        await _refresh();
    }

    public async Task AddCollectionAsync()
    {
        _setErrorMessage(string.Empty);
        try
        {
            if (_editingCollectionId is { } collectionId)
            {
                await _catalogService.UpdateCollectionAsync(_actor, collectionId, CollectionName);
                _editingCollectionId = null;
            }
            else
            {
                await _catalogService.AddCollectionAsync(_actor, CollectionName);
            }

            CollectionName = string.Empty;
            await _refresh();
        }
        catch (ArgumentException exception)
        {
            _setErrorMessage(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            _setErrorMessage(exception.Message);
        }
    }

    public async Task DeleteCollectionAsync(CatalogCollection collection)
    {
        if (!await _confirmDelete(
                $"Delete collection '{collection.Name}'? Its feed memberships are removed, but the feeds remain in the catalog and in profiles."))
        {
            return;
        }

        await _catalogService.DeleteCollectionAsync(_actor, collection.Id);
        await _refresh();
    }

    private void NotifyMergeCategoryProperties()
    {
        OnPropertyChanged(nameof(MergeTargetCategory));
        OnPropertyChanged(nameof(CanMergeCategory));
        OnPropertyChanged(nameof(MergeCategoryButtonLabel));
    }
}
