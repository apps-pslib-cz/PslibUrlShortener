using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;
using PslibUrlShortener.Services.Options;

namespace PslibUrlShortener.Areas.Admin.Pages.Owners
{
    public class IndexModel : PageModel
    {
        private readonly OwnerManager _ownerManager;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            OwnerManager ownerManager,
            ApplicationDbContext db,
            ILogger<IndexModel> logger,
            IOptions<ListingOptions> options)
        {
            _ownerManager = ownerManager ?? throw new ArgumentNullException(nameof(ownerManager));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ListingOptions = options.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public ListingOptions ListingOptions { get; set; }

        // --- Query parametry ---
        [BindProperty(SupportsGet = true)] public string? Search { get; set; }
        [BindProperty(SupportsGet = true)] public string? OrderBy { get; set; } = "LinksDesc";
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
            string Sub,
            string DisplayName,
            string? Email,
            int LinksCount,
            DateTime UpdatedAtUtc
        );

        public async Task OnGetAsync()
        {
            _logger.LogInformation("Owners list: Search={Search}, OrderBy={OrderBy}, PageNo={PageNo}, PageSize={PageSize}",
                Search, OrderBy, PageNo, PageSize);

            var allowed = ListingOptions.PageSizes.ToHashSet();
            var pageSize = PageSize.HasValue && allowed.Contains(PageSize.Value) ? PageSize!.Value : ListingOptions.DefaultPageSize;
            var pageNumber = Math.Max(1, PageNo ?? 1);

            // Základní dotaz na vlastníky
            var owners = _ownerManager
                .Query()
                .AsNoTracking();

            // Filtrování
            if (!string.IsNullOrWhiteSpace(Search))
            {
                var s = Search.Trim().ToLowerInvariant();
                owners = owners.Where(o =>
                    (o.DisplayName != null && o.DisplayName.ToLower().Contains(s)) ||
                    (o.Email != null && o.Email.ToLower().Contains(s)) ||
                    o.Sub.ToLower().Contains(s)
                );
            }

            // Celkový poèet (pro stránkování)
            TotalCount = await owners.CountAsync();

            // Projekce s poètem odkazù (poèítáme pouze nesmazané odkazy; upravte podle potøeby)
            var projected = owners.Select(o => new
            {
                o.Sub,
                DisplayName = string.IsNullOrWhiteSpace(o.DisplayName) ? "(bez jména)" : o.DisplayName!,
                o.Email,
                LinksCount = _db.Links.Count(l => l.OwnerSub == o.Sub && l.DeletedAt == null),
                o.UpdatedAtUtc
            });

            // Øazení
            projected = OrderBy switch
            {
                "NameAsc" => projected.OrderBy(x => x.DisplayName).ThenByDescending(x => x.UpdatedAtUtc),
                "NameDesc" => projected.OrderByDescending(x => x.DisplayName).ThenByDescending(x => x.UpdatedAtUtc),

                "EmailAsc" => projected.OrderBy(x => x.Email).ThenByDescending(x => x.UpdatedAtUtc),
                "EmailDesc" => projected.OrderByDescending(x => x.Email).ThenByDescending(x => x.UpdatedAtUtc),

                "LinksAsc" => projected.OrderBy(x => x.LinksCount).ThenByDescending(x => x.UpdatedAtUtc),
                _ /* LinksDesc */ => projected.OrderByDescending(x => x.LinksCount).ThenByDescending(x => x.UpdatedAtUtc),
            };

            // Stránkování
            PageNo = pageNumber;
            PageSize = pageSize;
            var skip = (pageNumber - 1) * pageSize;

            var list = await projected
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            Items = list.Select(x => new Row(
                x.Sub,
                x.DisplayName,
                x.Email,
                x.LinksCount,
                x.UpdatedAtUtc
            )).ToList();
        }
    }
}