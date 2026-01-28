using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;
using PslibUrlShortener.Services.Options;
using QRCoder;

namespace PslibUrlShortener.Areas.Admin.Pages.Links
{
    public class DetailsModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly ILogger<DetailsModel> _logger;
        private readonly IQrCodeService _qr;

        public DetailsModel(LinkManager linkManager, IQrCodeService qr, ILogger<DetailsModel> logger)
        {
            _linkManager = linkManager;
            _qr = qr;
            _logger = logger;
        }

        // Data pro UI
        public ViewModel Data { get; private set; } = default!;
        public string? ShortUrlPreview { get; private set; }

        // Query parametry pro stránkování hitů
        [BindProperty(SupportsGet = true)] public int? HitsPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int? HitsPageSize { get; set; } = 20;

        public async Task<IActionResult> OnGetAsync(int id)
        {
            // Link + vlastník
            var link = await _linkManager.Query()
                .AsNoTracking()
                .Include(l => l.Owner)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (link is null) return NotFound();

            // Postav náhled krátké URL (stejně jako jinde)
            var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
            try
            {
                ShortUrlPreview = _linkManager.GenerateShortUrl(link.Domain, link.Code, baseUrl);
            }
            catch
            {
                ShortUrlPreview = $"{baseUrl}/{link.Code}";
            }

            // Načti hity (stránkované, nejnovější první)
            var page = Math.Max(1, HitsPage ?? 1);
            var pageSize = Math.Clamp(HitsPageSize ?? 20, 5, 200);

            var hitsQuery = HttpContext.RequestServices
                .GetRequiredService<Data.ApplicationDbContext>()
                .LinkHits
                .AsNoTracking()
                .Where(h => h.LinkId == id);

            var totalHits = await hitsQuery.CountAsync();
            var items = await hitsQuery
                .OrderByDescending(h => h.AtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            Data = new ViewModel
            (
                Id: link.Id,
                Domain: link.Domain,
                Code: link.Code,
                TargetUrl: link.TargetUrl,
                Title: link.Title,
                Note: link.Note,
                OwnerDisplay: string.IsNullOrWhiteSpace(link.Owner?.DisplayName) ? "(no owner)" : link.Owner!.DisplayName!,
                OwnerEmail: link.Owner?.Email,
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

        // SoftDelete / Restore přímo z detailu
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostSoftDeleteAsync(int id)
        {
            try
            {
                var updated = await _linkManager.SoftDeleteAsync(id);
                if (updated == null) TempData["FailureMessage"] = "Odkaz nenalezen.";
                else TempData["SuccessMessage"] = "Odkaz byl označen jako smazaný.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Soft delete failed for Id={Id}", id);
                TempData["FailureMessage"] = "Soft mazání se nepodařilo.";
            }
            return RedirectToPage(new { id, HitsPage, HitsPageSize });
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostRestoreAsync(int id)
        {
            try
            {
                var updated = await _linkManager.RestoreAsync(id);
                if (updated == null) TempData["FailureMessage"] = "Odkaz nenalezen.";
                else TempData["SuccessMessage"] = "Odkaz byl obnoven.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed for Id={Id}", id);
                TempData["FailureMessage"] = "Obnovení se nepodařilo.";
            }
            return RedirectToPage(new { id, HitsPage, HitsPageSize });
        }

        // QR handler
        public async Task<IActionResult> OnGetQrAsync(
            int id,
            string fmt = "svg",
            int s = 256,
            bool inverse = false,
            string ecc = "M",
            bool qz = true,
            int? ver = null,
            string? fg = null,
            string? bg = null)
        {
            var link = await _linkManager.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id);
            if (link is null) return NotFound();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // Použij doménu odkazu (ne null), jinak QR nebude správně pro custom domény
            var url = _linkManager.GenerateShortUrl(link.Domain, link.Code, baseUrl);

            var eccLevel = ecc.ToUpperInvariant() switch
            {
                "L" => QRCodeGenerator.ECCLevel.L,
                "Q" => QRCodeGenerator.ECCLevel.Q,
                "H" => QRCodeGenerator.ECCLevel.H,
                _ => QRCodeGenerator.ECCLevel.M
            };

            var opt = new QrRenderOptions(
                EccLevel: eccLevel,
                DrawQuietZones: qz,
                RequestedVersion: ver,
                ForegroundHex: fg ?? (inverse ? "#FFFFFF" : "#000000"),
                BackgroundHex: bg ?? (inverse ? "#000000" : "#FFFFFF")
            );

            Response.Headers.CacheControl = "public, max-age=604800, immutable";

            if (fmt.Equals("png", StringComparison.OrdinalIgnoreCase))
            {
                return File(_qr.GeneratePng(url, s, opt), "image/png");
            }

            // Pro SVG promítneme požadovanou velikost do pixels-per-module
            opt = opt with { SvgSizePx = s };
            return Content(_qr.GenerateSvg(url, opt), "image/svg+xml; charset=utf-8");
        }

        public record ViewModel(
            int Id,
            string? Domain,
            string Code,
            string TargetUrl,
            string? Title,
            string? Note,
            string OwnerDisplay,
            string? OwnerEmail,
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