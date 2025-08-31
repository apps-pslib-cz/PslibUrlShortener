using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;

namespace PslibUrlShortener.Areas.Admin.Pages.Links
{
    public class DetailsModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(LinkManager linkManager, ILogger<DetailsModel> logger)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Data pro UI
        public ViewModel Data { get; private set; } = default!;
        public string? ShortUrlPreview { get; private set; }

        // Query parametry pro stránkování hitù
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

            // Postav náhled krátké URL (stejnì jako jinde)
            var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
            try
            {
                ShortUrlPreview = _linkManager.GenerateShortUrl(link.Domain, link.Code, baseUrl);
            }
            catch
            {
                ShortUrlPreview = $"{baseUrl}/{link.Code}";
            }

            // Naèti hity (stránkované, nejnovìjší první)
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

        // SoftDelete / Restore pøímo z detailu (stejnì jako na Indexu)
        public async Task<IActionResult> OnPostSoftDeleteAsync(int id)
        {
            try
            {
                var updated = await _linkManager.SoftDeleteAsync(id);
                if (updated == null) TempData["FailureMessage"] = "Odkaz nenalezen.";
                else TempData["SuccessMessage"] = "Odkaz byl oznaèen jako smazaný.";
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
            try
            {
                var updated = await _linkManager.RestoreAsync(id);
                if (updated == null) TempData["FailureMessage"] = "Odkaz nenalezen.";
                else TempData["SuccessMessage"] = "Odkaz byl obnoven.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed for Id={Id}", id);
                TempData["FailureMessage"] = "Obnovení se nepodaøilo.";
            }
            return RedirectToPage(new { id, HitsPage, HitsPageSize });
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