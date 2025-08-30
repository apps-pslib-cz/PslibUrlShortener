using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;

namespace PslibUrlShortener.Areas.Admin.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ApplicationDbContext db, ILogger<IndexModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        // --- Query parametry ---
        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public string? Owner { get; set; }
        [BindProperty(SupportsGet = true)] public string? Status { get; set; } = "all"; // all/enabled/disabled/active/scheduled/expired/deleted
        [BindProperty(SupportsGet = true)] public DateTime? CreatedFrom { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? CreatedTo { get; set; }
        [BindProperty(SupportsGet = true)] public string? OrderBy { get; set; } = "CreatedDesc";
        [BindProperty(SupportsGet = true)] public int? PageNo { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int? PageSize { get; set; } = 20;

        // --- Data pro UI ---
        public int TotalCount { get; set; }
        public List<int> PageSizeNumbers { get; } = new() { 10, 20, 50, 100 };
        public IReadOnlyList<Row> Items { get; private set; } = Array.Empty<Row>();

        public record Row(
            int Id,
            string Code,
            string TargetUrl,
            string ShortUrlDisplay,
            string OwnerDisplay,
            DateTime CreatedAt,
            long Clicks,
            bool IsEnabled,
            DateTime? ActiveFromUtc,
            DateTime? ActiveToUtc,
            DateTime? DeletedAtUtc
        );

        public async Task OnGetAsync()
        {
            var allowedSizes = PageSizeNumbers.ToHashSet();
            var size = (PageSize.HasValue && allowedSizes.Contains(PageSize.Value)) ? PageSize.Value : 20;
            var page = Math.Max(1, PageNo ?? 1);
            var now = DateTime.UtcNow;

            // LEFT JOIN na Owners kvùli zobrazovanému jménu
            var q = from l in _db.Links.AsNoTracking()
                    join o in _db.Owners.AsNoTracking() on l.OwnerSub equals o.Sub into prof
                    from o in prof.DefaultIfEmpty()
                    select new { l, o };

            // Filtr: smazané vs. nesmazané pøes Status
            if (!string.Equals(Status, "deleted", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(x => x.l.DeletedAt == null);
            }

            // Filtr: fulltext
            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim();
                q = q.Where(x =>
                    x.l.Code.Contains(s) ||
                    (x.l.TargetUrl != null && x.l.TargetUrl.Contains(s)) ||
                    (x.l.Title != null && x.l.Title.Contains(s)));
            }

            // Filtr: owner podle jména/emailu/sub
            if (!string.IsNullOrWhiteSpace(Owner))
            {
                var o = Owner.Trim();
                q = q.Where(x =>
                    (x.o != null && (
                        (x.o.DisplayName != null && x.o.DisplayName.Contains(o)) ||
                        (x.o.Email != null && x.o.Email.Contains(o))
                    )) ||
                    (x.l.OwnerName != null && x.l.OwnerName.Contains(o)) ||
                    x.l.OwnerSub.Contains(o));
            }

            // Filtr: status
            q = Status?.ToLowerInvariant() switch
            {
                "enabled" => q.Where(x => x.l.IsEnabled),
                "disabled" => q.Where(x => !x.l.IsEnabled),
                "active" => q.Where(x => x.l.IsEnabled
                                         && (x.l.ActiveFromUtc == null || x.l.ActiveFromUtc <= now)
                                         && (x.l.ActiveToUtc == null || x.l.ActiveToUtc > now)),
                "scheduled" => q.Where(x => x.l.IsEnabled
                                         && x.l.ActiveFromUtc != null && now < x.l.ActiveFromUtc),
                "expired" => q.Where(x => x.l.ActiveToUtc != null && now >= x.l.ActiveToUtc),
                "deleted" => q.Where(x => x.l.DeletedAt != null),
                _ => q
            };

            // Filtr: datum vytvoøení
            if (CreatedFrom.HasValue)
            {
                var f = DateTime.SpecifyKind(CreatedFrom.Value.Date, DateTimeKind.Utc);
                q = q.Where(x => x.l.CreatedAt >= f);
            }
            if (CreatedTo.HasValue)
            {
                var t = DateTime.SpecifyKind(CreatedTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                q = q.Where(x => x.l.CreatedAt <= t);
            }

            // Øazení
            q = OrderBy switch
            {
                "CodeAsc" => q.OrderBy(x => x.l.Code),
                "CodeDesc" => q.OrderByDescending(x => x.l.Code),
                "OwnerAsc" => q.OrderBy(x => x.o!.DisplayName ?? x.l.OwnerName ?? x.l.OwnerSub),
                "OwnerDesc" => q.OrderByDescending(x => x.o!.DisplayName ?? x.l.OwnerName ?? x.l.OwnerSub),
                "ClicksAsc" => q.OrderBy(x => x.l.Clicks),
                "ClicksDesc" => q.OrderByDescending(x => x.l.Clicks),
                "CreatedAsc" => q.OrderBy(x => x.l.CreatedAt),
                _ => q.OrderByDescending(x => x.l.CreatedAt) // CreatedDesc
            };

            TotalCount = await q.CountAsync();

            var items = await q
                .Skip((page - 1) * size)
                .Take(size)
                .Select(x => new Row(
                    x.l.Id,
                    x.l.Code,
                    x.l.TargetUrl,
                    "", // doplníme v PostProcess (URL potøebuje Request.Host)
                    x.o != null
                        ? (x.o.DisplayName ?? x.o.Email ?? x.l.OwnerName ?? x.l.OwnerSub)
                        : (x.l.OwnerName ?? x.l.OwnerSub),
                    x.l.CreatedAt,
                    x.l.Clicks,
                    x.l.IsEnabled,
                    x.l.ActiveFromUtc,
                    x.l.ActiveToUtc,
                    x.l.DeletedAt
                ))
                .ToListAsync();

            // dopoèítej krátkou URL až v aplikaci (kvùli Request.Host)
            Items = items.Select(i => i with { ShortUrlDisplay = BuildShortUrl(i.Code) }).ToList();

            PageNo = page;
            PageSize = size;
        }

        private string BuildShortUrl(string code)
        {
            var schemeHost = $"{Request.Scheme}://{Request.Host}";
            return $"{schemeHost}/{code}";
        }
    }
}
