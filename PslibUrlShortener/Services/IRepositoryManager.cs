namespace PslibUrlShortener.Services
{
    public interface IRepositoryManager<T, TKey>
    {
        Task<T?> CreateAsync(T entity);
        Task<T?> UpdateAsync(T entity);
        Task<bool> DeleteAsync(T entity);
        Task<T?> GetByIdAsync(TKey id, bool includeRelated);
        IQueryable<T> Query();
    }
}
