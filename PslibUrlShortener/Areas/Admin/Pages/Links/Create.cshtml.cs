using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;

namespace PslibUrlShortener.Areas.Admin.Pages.Links
{
    public class CreateModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(LinkManager linkManager, ILogger<CreateModel> logger)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        // pro náhled krátké adresy v UI
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
            if (!ModelState.IsValid)
            {
                BuildPreview();
                return Page();
            }

            try
            {
                var ownerSub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new InvalidOperationException("OwnerSub nelze urèit – nejste pøihlášeni?");

                var link = new Link
                {
                    Domain = string.IsNullOrWhiteSpace(Input.Domain) ? null : Input.Domain,
                    Code = string.IsNullOrWhiteSpace(Input.Code) ? string.Empty : Input.Code,
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
                    // obecná chyba ukládání – služba zalogovala detaily
                    ModelState.AddModelError(string.Empty, "Pøi vytváøení odkazu došlo k chybì.");
                    BuildPreview();
                    return Page();
                }

                SuccessMessage = "Odkaz byl vytvoøen.";
                return RedirectToPage("./Index");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Pro tuto doménu a kód už odkaz existuje"))
            {
                // hezká validace pro uživatele – kolize (Domain, Code)
                ModelState.AddModelError(nameof(Input.Code), "Pro tuto doménu a kód už odkaz existuje.");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Chyba databáze pøi vytváøení odkazu.");
                ModelState.AddModelError(string.Empty, "Chyba databáze pøi vytváøení odkazu.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Neoèekávaná chyba pøi vytváøení odkazu.");
                ModelState.AddModelError(string.Empty, "Neoèekávaná chyba pøi vytváøení odkazu.");
            }

            BuildPreview();
            return Page();
        }

        private void BuildPreview()
        {
            // baseUrl aplikace
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var domain = string.IsNullOrWhiteSpace(Input.Domain) ? null : Input.Domain;
            var code = string.IsNullOrWhiteSpace(Input.Code) ? "••••••" : Input.Code.Trim();

            try
            {
                // využijeme službu, aby se náhled choval stejnì jako produkèní generování
                ShortUrlPreview = _linkManager.GenerateShortUrl(domain, code, baseUrl);
            }
            catch
            {
                // pøi chybì generování jen fallback
                ShortUrlPreview = $"{baseUrl}/{code}";
            }
        }

        public class InputModel
        {
            [Display(Name = "Doména"), StringLength(255)]
            public string? Domain { get; set; }

            // Code je VOLITELNÝ – služba ho vygeneruje, když zùstane prázdný
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