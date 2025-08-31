using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;

namespace PslibUrlShortener.Services
{
    /// <summary>
    /// Správa tabulky Owners. PK = Sub (string).
    /// Kromě klasického CRUD umí "self-healing":
    /// - EnsureOwnerAsync(ClaimsPrincipal) → založí Owner při prvním přihlášení
    /// - UpsertFromClaimsAsync → aktualizuje DisplayName/Email podle Claims
    /// </summary>
    public class OwnerManager : IRepositoryManager<Owner, string>
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<OwnerManager> _logger;

        public OwnerManager(ApplicationDbContext db, ILogger<OwnerManager> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ----- High-level helpers -----

        /// <summary>
        /// Zajistí, že existuje Owner pro aktuálního uživatele.
        /// Vrací jeho Sub (PK).
        /// </summary>
        public async Task<string> EnsureOwnerAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? throw new InvalidOperationException("Chybí ClaimTypes.NameIdentifier (sub).");

            var owner = await _db.Owners.FirstOrDefaultAsync(o => o.Sub == sub, ct);
            if (owner != null) return sub;

            owner = FromClaims(user, sub);
            _db.Owners.Add(owner);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Vytvořen Owner pro {Sub}", sub);
            return sub;
        }

        /// <summary>
        /// Najde Owner podle Sub; pokud neexistuje, vytvoří z claims.
        /// Pokud existuje, může volitelně aktualizovat jméno/email z claims.
        /// </summary>
        public async Task<Owner> UpsertFromClaimsAsync(ClaimsPrincipal user, bool refreshProfile = true, CancellationToken ct = default)
        {
            var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? throw new InvalidOperationException("Chybí ClaimTypes.NameIdentifier (sub).");

            var owner = await _db.Owners.FirstOrDefaultAsync(o => o.Sub == sub, ct);
            if (owner == null)
            {
                owner = FromClaims(user, sub);
                _db.Owners.Add(owner);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Vytvořen Owner (upsert) pro {Sub}", sub);
                return owner;
            }

            if (refreshProfile)
            {
                var dn = user.FindFirstValue(ClaimTypes.Name);
                var em = user.FindFirstValue(ClaimTypes.Email);

                var changed = false;
                if (!string.IsNullOrWhiteSpace(dn) && dn != owner.DisplayName) { owner.DisplayName = dn; changed = true; }
                if (!string.IsNullOrWhiteSpace(em) && em != owner.Email) { owner.Email = em; changed = true; }

                if (changed)
                {
                    _db.Owners.Update(owner);
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("Aktualizován Owner (profil) pro {Sub}", sub);
                }
            }
            return owner;
        }

        public async Task<Owner?> GetBySubAsync(string sub, CancellationToken ct = default)
            => await _db.Owners.AsNoTracking().FirstOrDefaultAsync(o => o.Sub == sub, ct);

        // ----- IRepositoryManager<Owner, string> -----

        public async Task<Owner?> CreateAsync(Owner entity)
        {
            if (entity is null) throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrWhiteSpace(entity.Sub))
                throw new ArgumentException("Owner.Sub je povinné.", nameof(entity));

            var exists = await _db.Owners.AsNoTracking().AnyAsync(o => o.Sub == entity.Sub);
            if (exists) throw new InvalidOperationException("Owner se stejným Sub již existuje.");

            _db.Owners.Add(entity);
            try
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("Owner created: {Sub}", entity.Sub);
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Owner {Sub}", entity.Sub);
                return null;
            }
        }

        public async Task<Owner?> UpdateAsync(Owner entity)
        {
            if (entity is null) throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrWhiteSpace(entity.Sub))
                throw new ArgumentException("Owner.Sub je povinné.", nameof(entity));

            var exists = await _db.Owners.AsNoTracking().AnyAsync(o => o.Sub == entity.Sub);
            if (!exists) throw new ArgumentException("Owner neexistuje v databázi.", nameof(entity));

            _db.Owners.Update(entity);
            try
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("Owner updated: {Sub}", entity.Sub);
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update Owner {Sub}", entity.Sub);
                return null;
            }
        }

        public async Task<bool> DeleteAsync(Owner entity)
        {
            if (entity is null) throw new ArgumentNullException(nameof(entity));
            _db.Owners.Remove(entity);
            try
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("Owner deleted: {Sub}", entity.Sub);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete Owner {Sub}", entity.Sub);
                return false;
            }
        }

        public async Task<Owner?> GetByIdAsync(string id, bool includeRelated)
        {
            if (!includeRelated)
                return await _db.Owners.FindAsync(id);

            // Pokud někdy přidáte navigace, můžete je tady includovat
            return await _db.Owners.FirstOrDefaultAsync(o => o.Sub == id);
        }

        public IQueryable<Owner> Query() => _db.Owners.AsQueryable();

        // ----- Interní pomocníci -----

        private static Owner FromClaims(ClaimsPrincipal user, string sub) => new()
        {
            Sub = sub,
            DisplayName = user.FindFirstValue(ClaimTypes.Name) ?? "(unknown)",
            Email = user.FindFirstValue(ClaimTypes.Email) ?? null
        };
    }
}