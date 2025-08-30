using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;
using System.Security.Claims;

namespace PslibUrlShortener.Areas.Links.Pages
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
            bool IsEnabled,
            DateTime CreatedAt,
            long Clicks,
            DateTime? ActiveFromUtc,
            DateTime? ActiveToUtc,
            string ShortUrlDisplay
        );

        public async Task<IActionResult> OnGetAsync()
        {
            var sub = User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(sub)) return Forbid();

            var allowedSizes = PageSizeNumbers.ToHashSet();
            var size = (PageSize.HasValue && allowedSizes.Contains(PageSize.Value)) ? PageSize.Value : 20;
            var page = Math.Max(1, PageNo ?? 1);

            IQueryable<Link> q = _db.Links.AsNoTracking()
                .Where(l => l.DeletedAt == null && l.OwnerSub == sub);

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim();
                q = q.Where(l =>
                    l.Code.Contains(s) ||
                    (l.TargetUrl != null && l.TargetUrl.Contains(s)) ||
                    (l.Title != null && l.Title.Contains(s)));
            }

            q = OrderBy switch
            {
                "CodeAsc" => q.OrderBy(l => l.Code),
                "CodeDesc" => q.OrderByDescending(l => l.Code),
                "ClicksAsc" => q.OrderBy(l => l.Clicks),
                "ClicksDesc" => q.OrderByDescending(l => l.Clicks),
                "CreatedAsc" => q.OrderBy(l => l.CreatedAt),
                _ => q.OrderByDescending(l => l.CreatedAt) // "CreatedDesc"
            };

            TotalCount = await q.CountAsync();

            var items = await q.Skip((page - 1) * size).Take(size)
                .Select(l => new Row(
                    l.Id,
                    l.Code,
                    l.TargetUrl,
                    l.IsEnabled,
                    l.CreatedAt,
                    l.Clicks,
                    l.ActiveFromUtc,
                    l.ActiveToUtc,
                    ShortUrlDisplay(l)
                ))
                .ToListAsync();

            Items = items;
            PageNo = page;
            PageSize = size;

            return Page();
        }

        private string ShortUrlDisplay(Link l)
        {
            // Preferuj uloženou doménu; jinak aktuální host
            var baseUrl = !string.IsNullOrWhiteSpace(l.Domain)
                ? $"https://{l.Domain}"
                : $"{Request.Scheme}://{Request.Host}";
            return $"{baseUrl}/{l.Code}";
        }
    }
}