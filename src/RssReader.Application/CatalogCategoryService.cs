using RssReader.Domain;

namespace RssReader.Application;

internal sealed class CatalogCategoryService(ICatalogStore store)
{
    public async Task<CatalogCategory> AddCategoryAsync(
        Profile actor,
        string name,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        var normalizedName = CatalogServiceRules.RequireName(name, "category");
        var categories = await store.GetCategoriesAsync(cancellationToken);
        if (categories.Any(category => string.Equals(category.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That category already exists.");
        }

        var category = new CatalogCategory(Guid.NewGuid().ToString("N"), normalizedName);
        await store.AddCategoryAsync(category, cancellationToken);
        return category;
    }

    public async Task<CatalogCategory> UpdateCategoryAsync(
        Profile actor,
        string categoryId,
        string name,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        var normalizedName = CatalogServiceRules.RequireName(name, "category");
        var categories = await store.GetCategoriesAsync(cancellationToken);
        var existingCategory = categories.FirstOrDefault(category => category.Id == categoryId)
            ?? throw new InvalidOperationException("That category no longer exists.");
        if (categories.Any(category => category.Id != categoryId &&
            string.Equals(category.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That category already exists.");
        }

        var updatedCategory = existingCategory with { Name = normalizedName };
        await store.UpdateCategoryAsync(updatedCategory, cancellationToken);
        return updatedCategory;
    }

    public async Task MergeCategoriesAsync(
        Profile actor,
        string sourceCategoryId,
        string targetCategoryId,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        if (string.Equals(sourceCategoryId, targetCategoryId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Choose a different category to merge into.");
        }

        var categories = await store.GetCategoriesAsync(cancellationToken);
        if (categories.All(category => category.Id != sourceCategoryId) ||
            categories.All(category => category.Id != targetCategoryId))
        {
            throw new InvalidOperationException("The source or target category no longer exists.");
        }

        await store.MergeCategoriesAsync(sourceCategoryId, targetCategoryId, cancellationToken);
    }

    public async Task DeleteCategoryAsync(
        Profile actor,
        string categoryId,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        await store.DeleteCategoryAsync(categoryId, cancellationToken);
    }
}
