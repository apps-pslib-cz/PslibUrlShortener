using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;

namespace PslibUrlShortener.Areas.Admin.Pages.Links
{
    public class DeleteModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly ILogger<DeleteModel> _logger;

        public DeleteModel(LinkManager linkManager, ILogger<DeleteModel> logger)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        // Pro èitelný náhled
        public string? OwnerDisplay { get; private set; }
        public string? ShortUrlPreview { get; private set; }

        // --- Status zprávy ---
        [TempData] public string? SuccessMessage { get; set; }
        [TempData] public string? FailureMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var link = await _linkManager.GetByIdAsync(id, includeRelated: true);
            if (link is null) return NotFound();

            Input = new InputModel
            {
                Id = link.Id,
                Domain = link.Domain,
                Code = link.Code,
                TargetUrl = link.TargetUrl,
                Title = link.Title,
                Note = link.Note,
                CreatedAt = link.CreatedAt,
                ActiveFromUtc = link.ActiveFromUtc,
                ActiveToUtc = link.ActiveToUtc,
                IsEnabled = link.IsEnabled,
                Clicks = link.Clicks,
                DeletedAt = link.DeletedAt,
                RowVersion = link.RowVersion
            };

            OwnerDisplay = string.IsNullOrWhiteSpace(link.Owner?.DisplayName) ? "(no owner)" : link.Owner!.DisplayName!;
            BuildPreview();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                BuildPreview();
                return Page();
            }

            // 1) Naèíst aktuální entitu z DB
            var link = await _linkManager.GetByIdAsync(Input.Id, includeRelated: false);
            if (link is null) return NotFound();

            try
            {
                // 2) Kontrola soubìhu pøes RowVersion (optimistický zámek)
                if (Input.RowVersion != null && link.RowVersion != null &&
                    !Input.RowVersion.SequenceEqual(link.RowVersion))
                {
                    ModelState.AddModelError(string.Empty,
                        "Záznam byl mezitím zmìnìn jiným uživatelem. Obnovte stránku a zkuste to znovu.");
                    // Vyplòte zobrazení podle naètené entity
                    Input.Domain = link.Domain;
                    Input.Code = link.Code;
                    Input.TargetUrl = link.TargetUrl;
                    Input.Title = link.Title;
                    Input.Note = link.Note;
                    Input.CreatedAt = link.CreatedAt;
                    Input.ActiveFromUtc = link.ActiveFromUtc;
                    Input.ActiveToUtc = link.ActiveToUtc;
                    Input.IsEnabled = link.IsEnabled;
                    Input.Clicks = link.Clicks;
                    Input.DeletedAt = link.DeletedAt;
                    Input.RowVersion = link.RowVersion; // nabídneme poslední verzi
                    BuildPreview();
                    return Page();
                }

                // 3) Smazat
                var ok = await _linkManager.DeleteAsync(link);
                if (!ok)
                {
                    ModelState.AddModelError(string.Empty, "Smazání se nepodaøilo. Zkuste to prosím znovu.");
                    // Zùstaòte na stránce s pùvodními daty
                    BuildPreview();
                    return Page();
                }

                SuccessMessage = "Odkaz byl smazán.";
                return RedirectToPage("./Index");
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, "Záznam už byl zmìnìn nebo smazán nìkým jiným.");
                BuildPreview();
                return Page();
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(string.Empty, "Chyba databáze pøi mazání.");
                BuildPreview();
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Neoèekávaná chyba pøi mazání odkazu Id={Id}", Input.Id);
                ModelState.AddModelError(string.Empty, "Neoèekávaná chyba pøi mazání.");
                BuildPreview();
                return Page();
            }
        }


        private void BuildPreview()
        {
            var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
            var domain = string.IsNullOrWhiteSpace(Input.Domain) ? null : Input.Domain;
            var code = string.IsNullOrWhiteSpace(Input.Code) ? "••••••" : Input.Code!.Trim();

            try
            {
                ShortUrlPreview = _linkManager.GenerateShortUrl(domain, code, baseUrl);
            }
            catch
            {
                ShortUrlPreview = $"{baseUrl}/{code}";
            }
        }

        public class InputModel
        {
            public int Id { get; set; }

            public string? Domain { get; set; }
            public string? Code { get; set; }
            public string? TargetUrl { get; set; }
            public string? Title { get; set; }
            public string? Note { get; set; }

            public DateTime CreatedAt { get; set; }
            public DateTime? ActiveFromUtc { get; set; }
            public DateTime? ActiveToUtc { get; set; }
            public bool IsEnabled { get; set; }
            public long Clicks { get; set; }
            public DateTime? DeletedAt { get; set; }

            // Concurrency token
            public byte[]? RowVersion { get; set; }
        }
    }
}
