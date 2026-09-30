using Shared.Contracts;

namespace Catalog.Service;

public sealed class CatalogRepository
{
    public IReadOnlyList<ProductDto> GetAll() => Array.Empty<ProductDto>();
}
