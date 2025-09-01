using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;
using PslibUrlShortener.Services.Options;

namespace PslibUrlShortener.Areas.Links.Pages
{
    public class DetailsModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly OwnerManager _ownerManager;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<DetailsModel> _logger;
        private readonly IQrCodeService _qr;

        public DetailsModel(
            LinkManager linkManager, 
            OwnerManager ownerManager, 
            ApplicationDbContext db, 
            ILogger<DetailsModel> logger,
            IQrCodeService qr)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _ownerManager = ownerManager ?? throw new ArgumentNullException(nameof(ownerManager));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _qr = qr ?? throw new ArgumentNullException(nameof(qr));
        }

        // Data pro UI
        public ViewModel Data { get; private set; } = default!;
        public string? ShortUrlPreview { get; private set; }

        // Query parametry pro stránkování hitù
        [BindProperty(SupportsGet = true)] public int? HitsPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int? HitsPageSize { get; set; } = 20;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            // ovìøení vlastnictví
            var sub = await _ownerManager.EnsureOwnerAsync(User);

            var link = await _linkManager.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id && l.OwnerSub == sub);

            if (link is null)
            {
                TempData["FailureMessage"] = "Odkaz nenalezen.";
                return RedirectToPage("./Index");
            }

            // náhled krátké URL – pro bìžného uživatele vždy výchozí host
            var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
            try
            {
                ShortUrlPreview = _linkManager.GenerateShortUrl(null, link.Code, baseUrl);
            }
            catch
            {
                ShortUrlPreview = $"{baseUrl}/{link.Code}";
            }

            // hity (stránkované, nejnovìjší první)
            var page = Math.Max(1, HitsPage ?? 1);
            var pageSize = Math.Clamp(HitsPageSize ?? 20, 5, 200);

            var hitsQuery = _db.LinkHits
                .AsNoTracking()
                .Where(h => h.LinkId == id);

            var totalHits = await hitsQuery.CountAsync();
            var items = await hitsQuery
                .OrderByDescending(h => h.AtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            Data = new ViewModel(
                Id: link.Id,
                Code: link.Code,
                TargetUrl: link.TargetUrl,
                Title: link.Title,
                Note: link.Note,
                CreatedAt: link.CreatedAt,
                ActiveFromUtc: link.ActiveFromUtc,
                ActiveToUtc: link.ActiveToUtc,
                IsEnabled: link.IsEnabled,
                Clicks: link.Clicks,
                LastAccessAt: link.LastAccessAt,
                DeletedAt: link.DeletedAt,
                HitsPage: page,
                HitsPageSize: pageSize,
                HitsTotal: totalHits,
                Hits: items
            );

            return Page();
        }

        // SoftDelete / Restore – jen na vlastním odkazu
        public async Task<IActionResult> OnPostSoftDeleteAsync(int id)
        {
            var sub = await _ownerManager.EnsureOwnerAsync(User);
            var link = await _linkManager.GetByIdAsync(id, includeRelated: false);
            if (link is null || link.OwnerSub != sub)
            {
                TempData["FailureMessage"] = "Odkaz nenalezen.";
                return RedirectToPage("./Index");
            }

            try
            {
                var updated = await _linkManager.SoftDeleteAsync(id);
                TempData[updated is null ? "FailureMessage" : "SuccessMessage"] =
                    updated is null ? "Soft mazání se nepodaøilo." : "Odkaz byl oznaèen jako smazaný.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Soft delete failed for Id={Id}", id);
                TempData["FailureMessage"] = "Soft mazání se nepodaøilo.";
            }

            return RedirectToPage(new { id, HitsPage, HitsPageSize });
        }

        public async Task<IActionResult> OnPostRestoreAsync(int id)
        {
            var sub = await _ownerManager.EnsureOwnerAsync(User);
            var link = await _linkManager.GetByIdAsync(id, includeRelated: false);
            if (link is null || link.OwnerSub != sub)
            {
                TempData["FailureMessage"] = "Odkaz nenalezen.";
                return RedirectToPage("./Index");
            }

            try
            {
                var updated = await _linkManager.RestoreAsync(id);
                TempData[updated is null ? "FailureMessage" : "SuccessMessage"] =
                    updated is null ? "Obnovení se nepodaøilo." : "Odkaz byl obnoven.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed for Id={Id}", id);
                TempData["FailureMessage"] = "Obnovení se nepodaøilo.";
            }

            return RedirectToPage(new { id, HitsPage, HitsPageSize });
        }

        public async Task<IActionResult> OnGetQrAsync(int id, string fmt = "svg", int s = 256, QrPreset preset = QrPreset.Default)
        {
            var sub = await _ownerManager.EnsureOwnerAsync(User);
            var link = await _linkManager.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id && l.OwnerSub == sub);
            if (link is null) return NotFound();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var url = _linkManager.GenerateShortUrl(null, link.Code, baseUrl);

            // Cache – týden, immutable (zmìna kódu => nová URL => nové QR)
            Response.Headers.CacheControl = "public, max-age=604800, immutable";

            if (fmt.Equals("png", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = _qr.GeneratePng(url, s, preset);
                return File(bytes, "image/png");
            }
            else // default SVG
            {
                var svg = _qr.GenerateSvg(url, preset);
                return Content(svg, "image/svg+xml; charset=utf-8");
            }
        }

        public record ViewModel(
            int Id,
            string Code,
            string TargetUrl,
            string? Title,
            string? Note,
            DateTime CreatedAt,
            DateTime? ActiveFromUtc,
            DateTime? ActiveToUtc,
            bool IsEnabled,
            long Clicks,
            DateTime? LastAccessAt,
            DateTime? DeletedAt,

            // hits
            int HitsPage,
            int HitsPageSize,
            int HitsTotal,
            IReadOnlyList<LinkHit> Hits
        );
    }
}