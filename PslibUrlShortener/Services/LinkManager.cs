using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;
using System.Text;
using System.Security.Cryptography;
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

        // --- Public API ---

        public async Task<Link?> CreateAsync(Link entity)
        {
            if (entity is null) throw new ArgumentNullException(nameof(entity));
            NormalizeLink(entity);

            if (string.IsNullOrWhiteSpace(entity.Code))
                entity.Code = await GenerateUniqueCodeAsync(entity.Domain);

            await EnsureUniqueAsync(entity.Domain, entity.Code);

            _context.Links.Add(entity);

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Link created: {@Link}", new { entity.Id, entity.Domain, entity.Code });
                return entity;
            }
            catch (DbUpdateException dbex) when (IsDomainCodeUniqueViolation(dbex))
            {
                // fallback – kdyby nás doběhl race condition
                throw new InvalidOperationException("Pro tuto doménu a kód už odkaz existuje.", dbex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Link.");
                return null;
            }
        }

        public async Task<bool> DeleteAsync(Link entity)
        {
            if (entity is null) throw new ArgumentNullException(nameof(entity));

            _context.Links.Remove(entity);
            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Link deleted: {Id}", entity.Id);
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
            if (entity is null) throw new ArgumentNullException(nameof(entity));

            // Existence
            var exists = await _context.Links.AsNoTracking().AnyAsync(l => l.Id == entity.Id);
            if (!exists)
                throw new ArgumentException("Entity neexistuje v databázi.", nameof(entity));

            NormalizeLink(entity);

            // Zajištění kódu
            if (string.IsNullOrWhiteSpace(entity.Code))
                entity.Code = await GenerateUniqueCodeAsync(entity.Domain);

            // Unikátnost (vyloučíme sebe)
            var clash = await _context.Links
                .AsNoTracking()
                .AnyAsync(l =>
                    l.Id != entity.Id &&
                    l.Code == entity.Code &&
                    ((l.Domain == null && entity.Domain == null) || (l.Domain != null && l.Domain == entity.Domain)));

            if (clash)
                throw new InvalidOperationException("Pro tuto doménu a kód už odkaz existuje.");

            _context.Links.Update(entity);

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Link updated: {@Link}", new { entity.Id, entity.Domain, entity.Code });
                return entity;
            }
            catch (DbUpdateException dbex) when (IsDomainCodeUniqueViolation(dbex))
            {
                throw new InvalidOperationException("Pro tuto doménu a kód už odkaz existuje.", dbex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update Link.");
                return null;
            }
        }

        // --- Code/URL helpers ---

        public async Task<string> GenerateUniqueCodeAsync(string? domain, int length = 6)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = Random.Shared;

            while (true)
            {
                var code = new string(Enumerable.Repeat(chars, length)
                    .Select(s => s[random.Next(s.Length)]).ToArray());

                var taken = await _context.Links.AsNoTracking().AnyAsync(l =>
                    l.Code == code &&
                    ((l.Domain == null && NormalizeDomain(domain) == null) ||
                     (l.Domain != null && l.Domain == NormalizeDomain(domain))));

                if (!taken) return code;
            }
        }

        public string GenerateShortUrl(string? domain, string code, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code nesmí být prázdný.", nameof(code));
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Base URL nesmí být prázdný.", nameof(baseUrl));

            var dn = NormalizeDomain(domain);

            // pokud je 'domain' plný URL (http/https), použij ji
            if (!string.IsNullOrWhiteSpace(dn) &&
                (dn.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 dn.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return $"{dn.TrimEnd('/')}/{code}";
            }

            // jinak dn je host (sub)doména – přepiš host v baseUrl
            if (!string.IsNullOrWhiteSpace(dn))
            {
                // baseUrl by mělo být např. "https://app.example.cz"
                var uri = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/");
                var builder = new UriBuilder(uri.Scheme, dn, uri.Port);
                return $"{builder.Uri.AbsoluteUri.TrimEnd('/')}/{code}";
            }

            // fallback na baseUrl
            return $"{baseUrl.TrimEnd('/')}/{code}";
        }

        public async Task<Link?> SoftDeleteAsync(int id)
        {
            var entity = await _context.Links.FindAsync(id);
            if (entity == null) return null;

            if (entity.DeletedAt == null)
            {
                entity.DeletedAt = DateTime.UtcNow;
                _context.Links.Update(entity);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Link soft-deleted: {Id}", id);
            }
            return entity;
        }

        public async Task<Link?> RestoreAsync(int id)
        {
            var entity = await _context.Links.FindAsync(id);
            if (entity == null) return null;

            if (entity.DeletedAt != null)
            {
                entity.DeletedAt = null;
                _context.Links.Update(entity);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Link restored: {Id}", id);
            }
            return entity;
        }

        public async Task<Link?> ResolveForRedirectAsync(string requestScheme, string requestHost, string code, DateTime utcNow)
        {
            // normalizace vstupu
            var host = (requestHost ?? "").Trim().ToLowerInvariant();
            var origin = $"{requestScheme?.ToLowerInvariant()}://{host}";

            // kandidáti: přesná doména (jako host) NEBO doména jako plná URL se schématem,
            // případně záznamy bez domény (výchozí)
            var q = _context.Links.AsNoTracking().Where(l => l.Code == code);

            // preferenční pořadí: exact domain match > full origin match > null domain
            var candidates = await q
                .Where(l =>
                    (l.Domain == null) ||
                    (l.Domain != null && (
                        l.Domain.ToLower() == host ||
                        l.Domain.ToLower() == origin)))
                .OrderByDescending(l => l.Domain != null) // non-null před null
                .ThenByDescending(l => l.CreatedAt)
                .ToListAsync();

            if (candidates.Count == 0) return null;

            // vem prvního, který je aktivní & povolený
            foreach (var link in candidates)
            {
                if (!link.IsEnabled) continue;
                if (link.DeletedAt != null) continue;
                if (link.ActiveFromUtc.HasValue && utcNow < link.ActiveFromUtc.Value) continue;
                if (link.ActiveToUtc.HasValue && utcNow > link.ActiveToUtc.Value) continue;

                return link;
            }

            return null;
        }

        public async Task RegisterHitAndTouchAsync(
            int linkId,
            string? referer,
            string? userAgent,
            string? remoteIp,   // může být IPv4/IPv6
            bool isBotHeuristic,
            DateTime utcNow)
        {
            // zvýšení clicků + last access + insert hit – jedna SaveChanges
            var entity = await _context.Links.FirstOrDefaultAsync(l => l.Id == linkId);
            if (entity == null) return;

            entity.Clicks += isBotHeuristic ? 0 : 1; // chcete-li nepočítat boty – jinak dejte vždy +1
            entity.LastAccessAt = utcNow;

            var hit = new LinkHit
            {
                LinkId = linkId,
                AtUtc = utcNow,
                Referer = TrimTo(referer, 2048),
                UserAgent = TrimTo(userAgent, 512),
                RemoteIpHash = HashIp(remoteIp),
                IsBot = isBotHeuristic
            };

            _context.LinkHits.Add(hit);
            await _context.SaveChangesAsync();
        }

        private static string? TrimTo(string? s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

        private static byte[]? HashIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return null;
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(ip));
        }

        public static bool LooksLikeBot(string? userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return false;
            var ua = userAgent.ToLowerInvariant();
            // heuristika – přidejte si vlastní signatury
            string[] keys = { "bot", "spider", "crawl", "crawler", "slurp", "bingpreview", "ahrefs", "facebookexternalhit", "whatsapp", "telegrambot" };
            foreach (var k in keys) if (ua.Contains(k)) return true;
            return false;
        }

        // --- Interní pomocníci ---

        private async Task EnsureUniqueAsync(string? domain, string code)
        {
            var dn = NormalizeDomain(domain);
            var exists = await _context.Links.AsNoTracking().AnyAsync(l =>
                l.Code == code &&
                ((l.Domain == null && dn == null) || (l.Domain != null && l.Domain == dn)));

            if (exists)
                throw new InvalidOperationException("Pro tuto doménu a kód už odkaz existuje.");
        }

        private static string? NormalizeDomain(string? domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return null;
            var d = domain.Trim();

            // Pokud je to čistě host (bez schématu), znormalizujeme na lowercase.
            // Plné URL necháme být (schéma + host), jen ořízneme whitespace.
            if (!(d.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                  d.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                d = d.ToLowerInvariant();
            }
            return string.IsNullOrWhiteSpace(d) ? null : d;
        }

        private static string NormalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Kód nesmí být prázdný.", nameof(code));
            return code.Trim(); // kód nedáváme na lowercase — může být case-sensitive
        }

        private void NormalizeLink(Link e)
        {
            e.Domain = NormalizeDomain(e.Domain);
            if (!string.IsNullOrWhiteSpace(e.Code))
                e.Code = NormalizeCode(e.Code);

            if (!string.IsNullOrWhiteSpace(e.TargetUrl))
                e.TargetUrl = e.TargetUrl.Trim();

            if (!string.IsNullOrWhiteSpace(e.Title))
                e.Title = e.Title!.Trim();

            if (!string.IsNullOrWhiteSpace(e.Note))
                e.Note = e.Note!.Trim();
        }

        private static bool IsDomainCodeUniqueViolation(DbUpdateException ex)
        {
            // Heuristika: hledej název indexu nebo „unique constraint“ v message
            var msg = ex.InnerException?.Message ?? ex.Message;
            return msg.Contains("IX_", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("unique", StringComparison.OrdinalIgnoreCase);
        }

        private IQueryable<Link> QueryWithIncludes() =>
            _context.Links
                .Include(l => l.Owner)
                .Include(l => l.Hits);
    }
}