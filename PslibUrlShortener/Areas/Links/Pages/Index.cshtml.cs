using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;
using PslibUrlShortener.Services.Options;

namespace PslibUrlShortener.Areas.Links.Pages
{
    public class IndexModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly OwnerManager _ownerManager;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            LinkManager linkManager,
            OwnerManager ownerManager,
            ILogger<IndexModel> logger,
            IOptions<ListingOptions> options)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _ownerManager = ownerManager ?? throw new ArgumentNullException(nameof(ownerManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ListingOptions = options.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public ListingOptions ListingOptions { get; set; }

        // --- Query parametry (bez Domény/Owner) ---
        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public bool? Enabled { get; set; }
        [BindProperty(SupportsGet = true)] public string? OrderBy { get; set; } = "CreatedDesc";
        [BindProperty(SupportsGet = true)] public int? PageNo { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int? PageSize { get; set; } = 20;

        // --- Data pro UI ---
        public int TotalCount { get; set; }
        public List<int> PageSizeNumbers { get; } = new() { 10, 20, 50, 100 };
        public IReadOnlyList<Row> Items { get; private set; } = Array.Empty<Row>();

        // --- Status zprávy ---
        [TempData] public string? SuccessMessage { get; set; }
        [TempData] public string? FailureMessage { get; set; }

        public record Row(
            int Id,
            string Code,
            string TargetUrl,
            string? Title,
            string ShortUrlDisplay,
            DateTime CreatedAt,
            long Clicks,
            DateTime? LastAccessAtUtc,
            bool IsEnabled,
            DateTime? ActiveFromUtc,
            DateTime? ActiveToUtc,
            DateTime? DeletedAtUtc
        );

        public async Task OnGetAsync()
        {
            _logger.LogInformation("User Links OnGet: Search={Search}, OrderBy={OrderBy}, PageNo={PageNo}, PageSize={PageSize}",
                Search, OrderBy, PageNo, PageSize);

            try
            {
                // Zajisti existenci Ownera a zjisti jeho Sub (PK)
                var sub = await _ownerManager.EnsureOwnerAsync(User);

                var allowedSizes = ListingOptions.PageSizes.ToHashSet();
                var pageSize = PageSize.HasValue && allowedSizes.Contains(PageSize.Value)
                    ? PageSize.Value
                    : ListingOptions.DefaultPageSize;

                var pageNumber = Math.Max(1, PageNo ?? 1);

                IQueryable<Link> query = _linkManager
                    .Query()
                    .AsNoTracking()
                    .Where(l => l.OwnerSub == sub); // <<< KLÍČOVÉ: jen moje odkazy

                // Filtrování (bez Domény/Ownera)
                if (!string.IsNullOrWhiteSpace(Search))
                {
                    var s = Search.Trim().ToLowerInvariant();
                    query = query.Where(l =>
                        l.Code.ToLower().Contains(s) ||
                        l.TargetUrl.ToLower().Contains(s) ||
                        (l.Note != null && l.Note.ToLower().Contains(s)) ||
                        (l.Title != null && l.Title.ToLower().Contains(s))
                    );
                }
                if (Enabled.HasValue)
                {
                    query = query.Where(l => l.IsEnabled == Enabled.Value);
                }

                // Celkový počet
                TotalCount = await query.CountAsync();

                // Řazení
                query = OrderBy switch
                {
                    "CreatedAsc" => query.OrderBy(l => l.CreatedAt),
                    "ClicksDesc" => query.OrderByDescending(l => l.Clicks).ThenByDescending(l => l.CreatedAt),
                    "ClicksAsc" => query.OrderBy(l => l.Clicks).ThenByDescending(l => l.CreatedAt),
                    "CodeAsc" => query.OrderBy(l => l.Code),
                    "CodeDesc" => query.OrderByDescending(l => l.Code),
                    "TargetAsc" => query.OrderBy(l => l.TargetUrl),
                    "TargetDesc" => query.OrderByDescending(l => l.TargetUrl),
                    _ => query.OrderByDescending(l => l.CreatedAt), // CreatedDesc
                };

                // Stránkování
                PageNo = pageNumber;
                PageSize = pageSize;
                var skip = (pageNumber - 1) * pageSize;
                query = query.Skip(skip).Take(pageSize);

                // Projekce (doména je vždy výchozí/empty → krátká URL z aktuálního hosta)
                var list = await query.ToListAsync();
                Items = list.Select(l => new Row(
                    l.Id,
                    l.Code,
                    l.TargetUrl,
                    l.Title,
                    BuildShortUrl(l.Code),
                    l.CreatedAt,
                    l.Clicks,
                    l.LastAccessAt,
                    l.IsEnabled,
                    l.ActiveFromUtc,
                    l.ActiveToUtc,
                    l.DeletedAt
                )).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chyba při načítání uživatelského seznamu odkazů.");
                FailureMessage = "Nepodařilo se načíst seznam odkazů. Zkuste to prosím znovu.";
            }
        }

        public async Task<IActionResult> OnPostSoftDeleteAsync(int id)
        {
            // Bezpečnost: soft-delete dovolíme jen na vlastním odkazu
            var sub = await _ownerManager.EnsureOwnerAsync(User);
            var link = await _linkManager.GetByIdAsync(id, includeRelated: false);

            if (link is null || link.OwnerSub != sub)
            {
                FailureMessage = "Odkaz nenalezen.";
                return RedirectToPage();
            }

            var updated = await _linkManager.SoftDeleteAsync(id);
            SuccessMessage = updated is not null ? "Odkaz byl označen jako smazaný." : "Nepodařilo se změnit stav odkazu.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRestoreAsync(int id)
        {
            var sub = await _ownerManager.EnsureOwnerAsync(User);
            var link = await _linkManager.GetByIdAsync(id, includeRelated: false);

            if (link is null || link.OwnerSub != sub)
            {
                FailureMessage = "Odkaz nenalezen.";
                return RedirectToPage();
            }

            var updated = await _linkManager.RestoreAsync(id);
            SuccessMessage = updated is not null ? "Odkaz byl obnoven." : "Nepodařilo se obnovit odkaz.";
            return RedirectToPage();
        }

        private string BuildShortUrl(string code)
        {
            var scheme = Request.Scheme;            // "http" nebo "https"
            var host = Request.Host.ToString();     // výchozí host (uživatel nemá custom domény)
            return $"{scheme}://{host}/{code}";
        }
    }
}