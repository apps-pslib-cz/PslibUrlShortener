using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;

namespace PslibUrlShortener.Areas.Links.Pages
{
    public class CreateModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly OwnerManager _ownerManager;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(LinkManager linkManager, OwnerManager ownerManager, ILogger<CreateModel> logger)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _ownerManager = ownerManager ?? throw new ArgumentNullException(nameof(ownerManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        // náhled krátké adresy v UI
        public string? ShortUrlPreview { get; private set; }

        // --- Status zprávy ---
        [TempData] public string? SuccessMessage { get; set; }
        [TempData] public string? FailureMessage { get; set; }

        public void OnGet()
        {
            BuildPreview();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // server-side validace rozsahu platnosti
            if (Input.ActiveFromUtc is not null && Input.ActiveToUtc is not null &&
                Input.ActiveToUtc < Input.ActiveFromUtc)
            {
                ModelState.AddModelError(nameof(Input.ActiveToUtc), "Konec platnosti nesmí být dřív než začátek.");
            }

            if (!ModelState.IsValid)
            {
                BuildPreview();
                return Page();
            }

            try
            {
                // přihlášený uživatel musí existovat jako Owner
                var ownerSub = await _ownerManager.EnsureOwnerAsync(User);

                var link = new Link
                {
                    Domain = null, // <<< klíčové: běžný uživatel nemá vlastní doménu
                    Code = string.IsNullOrWhiteSpace(Input.Code) ? string.Empty : Input.Code!.Trim(),
                    TargetUrl = Input.TargetUrl!,
                    Title = string.IsNullOrWhiteSpace(Input.Title) ? null : Input.Title,
                    Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note,
                    ActiveFromUtc = Input.ActiveFromUtc,
                    ActiveToUtc = Input.ActiveToUtc,
                    IsEnabled = Input.IsEnabled,
                    OwnerSub = ownerSub
                };

                var created = await _linkManager.CreateAsync(link);
                if (created is null)
                {
                    ModelState.AddModelError(string.Empty, "Při vytváření odkazu došlo k chybě.");
                    BuildPreview();
                    return Page();
                }

                SuccessMessage = "Odkaz byl vytvořen.";
                return RedirectToPage("./Index");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("existuje", StringComparison.OrdinalIgnoreCase))
            {
                // kolize (Code už existuje pro výchozí doménu)
                ModelState.AddModelError(nameof(Input.Code), "Krátký kód už existuje. Zvolte jiný.");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Chyba databáze při vytváření odkazu.");
                ModelState.AddModelError(string.Empty, "Chyba databáze při vytváření odkazu.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Neočekávaná chyba při vytváření odkazu.");
                ModelState.AddModelError(string.Empty, "Neočekávaná chyba při vytváření odkazu.");
            }

            BuildPreview();
            return Page();
        }

        private void BuildPreview()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var code = string.IsNullOrWhiteSpace(Input.Code) ? "••••••" : Input.Code!.Trim();

            try
            {
                // jednotné generování (doména je vždy null → použije se host aplikace)
                ShortUrlPreview = _linkManager.GenerateShortUrl(null, code, baseUrl);
            }
            catch
            {
                ShortUrlPreview = $"{baseUrl}/{code}";
            }
        }

        public class InputModel
        {
            // Code je VOLITELNÝ – služba ho vygeneruje, když zůstane prázdný
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
            public bool IsEnabled { get; set; } = true;
        }
    }
}