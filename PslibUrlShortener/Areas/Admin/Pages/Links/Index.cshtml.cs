using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;
using PslibUrlShortener.Services.Options;

namespace PslibUrlShortener.Areas.Admin.Pages.Links
{
    public class IndexModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(LinkManager linkManager, ILogger<IndexModel> logger, IOptions<ListingOptions> options)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ListingOptions = options.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public ListingOptions ListingOptions { get; set; }

        // --- Query parametry ---
        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public string? Short { get; set; }
        [BindProperty(SupportsGet = true)] public string? Domain { get; set; }
        [BindProperty(SupportsGet = true)] public string? Target { get; set; }
        [BindProperty(SupportsGet = true)] public string? Owner { get; set; }
        [BindProperty(SupportsGet = true)] public bool? Enabled { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? CreatedFrom { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? CreatedTo { get; set; }
        [BindProperty(SupportsGet = true)] public string? OrderBy { get; set; } = "CreatedDesc";
        [BindProperty(SupportsGet = true)] public int? PageNo { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int? PageSize { get; set; } = 20;

        // --- Data pro UI ---
        public int TotalCount { get; set; }
        public List<int> PageSizeNumbers { get; } = new() { 10, 20, 50, 100 };
        public IReadOnlyList<Row> Items { get; private set; } = Array.Empty<Row>();

        // --- Status zprávy ---
        [TempData]
        public string? SuccessMessage { get; set; }

        [TempData]
        public string? FailureMessage { get; set; }
        public record Row(
            int Id,
            string? Domain,
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
            _logger.LogInformation("OnGet called with parameters: Search={Search}, Short={Short}, Targer={Target} OrderBy={OrderBy}, PageNo={PageNo}, PageSize={PageSize}",
                Search, Short, Target, OrderBy, PageNo, PageSize);

            try
            {
                var allowedSizes = ListingOptions.PageSizes.ToHashSet();
                var pageSize = PageSize.HasValue && allowedSizes.Contains(PageSize.Value)
                    ? PageSize.Value
                    : ListingOptions.DefaultPageSize;

                var pageNumber = Math.Max(1, PageNo ?? 1);

                IQueryable<Link> query = _linkManager
                    .Query()
                    .AsNoTracking()
                    .Include(l => l.Owner);

                // Filtry
                if (!string.IsNullOrWhiteSpace(Search))
                {
                    var s = Search.Trim().ToLowerInvariant();
                    query = query.Where(l =>
                        l.Domain != null && l.Domain.ToLower().Contains(s) || 
                        l.Code.ToLower().Contains(s) ||
                        l.TargetUrl.ToLower().Contains(s) ||
                        l.Owner.DisplayName != null && l.Owner.DisplayName.ToLower().Contains(s) ||
                        l.Note != null && l.Note.ToLower().Contains(s) ||
                        l.Title != null && l.Title.ToLower().Contains(s)
                    );
                }
                if (!string.IsNullOrWhiteSpace(Domain))
                {
                    var s = Domain.Trim().ToLowerInvariant();
                    query = query.Where(l => l.Domain != null && l.Domain.ToLower().Contains(s));
                }

                if (!string.IsNullOrWhiteSpace(Short))
                {
                    var s = Short.Trim().ToLowerInvariant();
                    query = query.Where(l => l.Code.ToLower().Contains(s));
                }
                if (!string.IsNullOrWhiteSpace(Target))
                {
                    var s = Target.Trim().ToLowerInvariant();
                    query = query.Where(l => l.TargetUrl.ToLower().Contains(s));
                }
                if (!string.IsNullOrWhiteSpace(Owner))
                {
                    var s = Owner.Trim().ToLowerInvariant();
                    query = query.Where(l => l.Owner.DisplayName != null && l.Owner.DisplayName.ToLower().Contains(s));
                }
                if (Enabled.HasValue)
                {
                    query = query.Where(l => l.IsEnabled == Enabled.Value);
                }
                if (CreatedFrom.HasValue)
                {
                    var from = CreatedFrom.Value.Date;
                    query = query.Where(l => l.CreatedAt >= from);
                }
                if (CreatedTo.HasValue)
                {
                    var to = CreatedTo.Value.Date.AddDays(1);
                    query = query.Where(l => l.CreatedAt < to);
                }
                // Celkový poèet
                TotalCount = await query.CountAsync();

                // Øazení
                query = OrderBy switch
                {
                    "CreatedAsc" => query.OrderBy(l => l.CreatedAt),
                    "ClicksDesc" => query.OrderByDescending(l => l.Clicks).ThenByDescending(l => l.CreatedAt),
                    "ClicksAsc" => query.OrderBy(l => l.Clicks).ThenByDescending(l => l.CreatedAt),
                    "CodeAsc" => query.OrderBy(l => l.Code),
                    "CodeDesc" => query.OrderByDescending(l => l.Code),
                    "OwnerAsc" => query.OrderBy(l => l.Owner.DisplayName).ThenByDescending(l => l.CreatedAt),
                    "OwnerDesc" => query.OrderByDescending(l => l.Owner.DisplayName).ThenByDescending(l => l.CreatedAt),
                    "TargetAsc" => query.OrderBy(l => l.TargetUrl),
                    "TargetDesc" => query.OrderByDescending(l => l.TargetUrl),
                    "DomainAsc" => query.OrderBy(l => l.Domain).ThenByDescending(l => l.CreatedAt),     // <— NEW
                    "DomainDesc" => query.OrderByDescending(l => l.Domain).ThenByDescending(l => l.CreatedAt), // <— NEW
                    _ => query.OrderByDescending(l => l.CreatedAt), // CreatedDesc
                };


                // Stránkování
                PageNo = pageNumber;
                PageSize = pageSize;
                var skip = (pageNumber - 1) * pageSize;
                query = query.Skip(skip).Take(pageSize);

                // Projekce
                var list = await query.ToListAsync();
                Items = list.Select(l => new Row(
                    l.Id,
                    l.Domain,                                                  // <— NEW
                    l.Code,
                    l.TargetUrl,
                    BuildShortUrl(l.Domain, l.Code),                           // <— CHANGED
                    string.IsNullOrWhiteSpace(l.Owner?.DisplayName) ? "(no owner)" : l.Owner!.DisplayName!,
                    l.CreatedAt,
                    l.Clicks,
                    l.IsEnabled,
                    l.ActiveFromUtc,
                    l.ActiveToUtc,
                    l.DeletedAt
                )).ToList();

                _logger.LogInformation("Fetched {Count} links for page {PageNo} with page size {PageSize}. Total count: {TotalCount}",
                    Items.Count, PageNo, PageSize, TotalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching links.");
                FailureMessage = "An error occurred while fetching the links. Please try again later.";

            }
        }

        public async Task<IActionResult> OnPostSoftDeleteAsync(int id)
        {
            var link = await _linkManager.SoftDeleteAsync(id);
            if (link == null) FailureMessage = "Odkaz nenalezen.";
            else SuccessMessage = "Odkaz byl oznaèen jako smazaný.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRestoreAsync(int id)
        {
            var link = await _linkManager.RestoreAsync(id);
            if (link == null) FailureMessage = "Odkaz nenalezen.";
            else SuccessMessage = "Odkaz byl obnoven.";
            return RedirectToPage();
        }

        private string BuildShortUrl(string? domain, string code)
        {
            if (!string.IsNullOrWhiteSpace(domain) && (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return $"{domain.TrimEnd('/')}/{code}";
            }

            var scheme = Request.Scheme; // "http" nebo "https"
            var host = !string.IsNullOrWhiteSpace(domain) ? domain!.TrimEnd('/') : Request.Host.ToString();
            return $"{scheme}://{host}/{code}";
        }

    }
}
