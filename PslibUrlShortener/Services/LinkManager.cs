using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;

namespace PslibUrlShortener.Services
{
    public class LinkManager : IRepositoryManager<Link, int>
    {
        private readonly ILogger<LinkManager> _logger;
        private readonly ApplicationDbContext _context;

        public LinkManager(ILogger<LinkManager> logger, ApplicationDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Link?> CreateAsync(Link entity)
        {
            if (entity == null)
            {
                _logger.LogError("Attempted to create a null Link entity.");
                throw new ArgumentNullException(nameof(entity));
            }
            if (string.IsNullOrWhiteSpace(entity.Code))
            {
                entity.Code = GenerateUniqueCode();
            }
            else
            {
                if (_context.Links.Any(l => l.Code == entity.Code))
                {
                    _logger.LogError("Attempted to create a Link with a duplicate code: {Code}", entity.Code);
                    throw new ArgumentException("The provided code is already in use.", nameof(entity.Code));
                }
            }
            _context.Links.Add(entity);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Link created successfully.");
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Link.");
                return null;
            }
        }

        public async Task<bool> DeleteAsync(Link entity)
        {
            if (entity == null)
            {
                _logger.LogError("Attempted to delete a null Link entity.");
                throw new ArgumentNullException(nameof(entity));
            }

            _context.Links.Remove(entity);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Link deleted successfully.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete Link.");
                return false;
            }
        }

        public async Task<Link?> GetByIdAsync(int id, bool includeRelated = false)
        {
            if (includeRelated)
                return await QueryWithIncludes().FirstOrDefaultAsync(r => r.Id == id);

            return await _context.Links.FindAsync(id);
        }

        public Task<Link?> GetByIdAsync(int id) => GetByIdAsync(id, includeRelated: false);

        public IQueryable<Link> Query() => _context.Links.AsQueryable();

        public async Task<Link?> UpdateAsync(Link entity)
        {
            if (entity == null)
            {
                _logger.LogError("Attempted to update a null Link entity.");
                throw new ArgumentNullException(nameof(entity));
            }

            if (!_context.Links.Any(l => l.Id == entity.Id))
            {
                _logger.LogError("Attempted to update a non-existent Link entity with Id: {Id}", entity.Id);
                throw new ArgumentException("The entity does not exist in the database.", nameof(entity));
            }
            if (!string.IsNullOrWhiteSpace(entity.Code))
            {
                if (_context.Links.Any(l => l.Code == entity.Code && l.Id != entity.Id))
                {
                    _logger.LogError("Attempted to update a Link with a duplicate code: {Code}", entity.Code);
                    throw new ArgumentException("The provided code is already in use.", nameof(entity.Code));
                }
            }
            else
            {
                entity.Code = GenerateUniqueCode();
            }
            _context.Links.Update(entity);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Link updated successfully.");
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update Link.");
                return null;
            }
        }

        public string GenerateRandomCode()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = Random.Shared;
            string code;
            do
            {
                code = new string(Enumerable.Repeat(chars, 6)
                  .Select(s => s[random.Next(s.Length)]).ToArray());
            } while (_context.Links.Any(l => l.Code == code));
            return code;
        }

        public string GenerateShortUrl(string code, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL cannot be null or empty.", nameof(baseUrl));
            return $"{baseUrl.TrimEnd('/')}/{code}";
        }

        public string GenerateUniqueCode()
        {
            string code;
            do
            {
                code = GenerateRandomCode();
            } while (_context.Links.Any(l => l.Code == code));
            return code;
        }

        // -------- Helpers --------

        private IQueryable<Link> QueryWithIncludes() =>
            _context.Links
                .Include(l => l.Owner)
                .Include(l => l.Hits);
    }
}
