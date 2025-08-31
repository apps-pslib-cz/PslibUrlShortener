using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;

namespace PslibUrlShortener.Areas.Admin.Pages.Links
{
    public class EditModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly ILogger<EditModel> _logger;

        public EditModel(LinkManager linkManager, ILogger<EditModel> logger)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        // pro UI
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
                ActiveFromUtc = link.ActiveFromUtc,
                ActiveToUtc = link.ActiveToUtc,
                IsEnabled = link.IsEnabled,
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

            // Načteme původní kvůli OwnerSub (nemění se) a případně validaci existence
            var original = await _linkManager.GetByIdAsync(Input.Id, includeRelated: false);
            if (original is null) return NotFound();

            try
            {
                var toUpdate = new Link
                {
                    Id = Input.Id,
                    Domain = string.IsNullOrWhiteSpace(Input.Domain) ? null : Input.Domain,
                    Code = string.IsNullOrWhiteSpace(Input.Code) ? string.Empty : Input.Code,
                    TargetUrl = Input.TargetUrl!,
                    Title = string.IsNullOrWhiteSpace(Input.Title) ? null : Input.Title,
                    Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note,
                    ActiveFromUtc = Input.ActiveFromUtc,
                    ActiveToUtc = Input.ActiveToUtc,
                    IsEnabled = Input.IsEnabled,
                    OwnerSub = original.OwnerSub,       // neměnit
                    CreatedAt = original.CreatedAt,     // pro jistotu zachovat
                    RowVersion = Input.RowVersion       // pro souběh
                };

                var updated = await _linkManager.UpdateAsync(toUpdate);
                if (updated is null)
                {
                    // Služba loguje detaily; tady zobrazíme přátelskou zprávu
                    ModelState.AddModelError(string.Empty, "Při ukládání došlo k chybě. Zkuste to prosím znovu.");
                    BuildPreview();
                    return Page();
                }

                SuccessMessage = "Odkaz byl uložen.";
                return RedirectToPage("./Index");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Pro tuto doménu a kód už odkaz existuje"))
            {
                ModelState.AddModelError(nameof(Input.Code), "Pro tuto doménu a kód už odkaz existuje.");
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, "Záznam byl mezitím změněn jiným uživatelem. Načtěte stránku a zkuste znovu.");
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(string.Empty, "Chyba databáze při ukládání.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Neočekávaná chyba při ukládání odkazu Id={Id}", Input.Id);
                ModelState.AddModelError(string.Empty, "Neočekávaná chyba při ukládání.");
            }

            BuildPreview();
            return Page();
        }

        private void BuildPreview()
        {
            var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
            var domain = string.IsNullOrWhiteSpace(Input.Domain) ? null : Input.Domain;
            var code = string.IsNullOrWhiteSpace(Input.Code) ? string.IsNullOrWhiteSpace(Request.Query["code"]) ? "••••••" : Input.Code : Input.Code;

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
            [Required]
            public int Id { get; set; }

            [Display(Name = "Doména"), StringLength(255)]
            public string? Domain { get; set; }

            // Code může být prázdný → služba vygeneruje (zachováváme stejné chování jako na Create)
            [Display(Name = "Krátký kód"), StringLength(16)]
            public string? Code { get; set; }

            [Required, StringLength(2048), Display(Name = "Cílová adresa (URL)")]
            [Url(ErrorMessage = "Zadejte platnou URL adresu.")]
            public string? TargetUrl { get; set; }

            [Display(Name = "Titulek"), StringLength(256)]
            public string? Title { get; set; }

            [Display(Name = "Poznámka"), StringLength(1024)]
            public string? Note { get; set; }

            [Display(Name = "Aktivní od (UTC)")]
            public DateTime? ActiveFromUtc { get; set; }

            [Display(Name = "Aktivní do (UTC)")]
            public DateTime? ActiveToUtc { get; set; }

            [Display(Name = "Povolený odkaz")]
            public bool IsEnabled { get; set; }

            // Concurrency token
            public byte[]? RowVersion { get; set; }
        }
    }
}