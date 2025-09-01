using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;

namespace PslibUrlShortener.Areas.Links.Pages
{
    public class EditModel : PageModel
    {
        private readonly LinkManager _linkManager;
        private readonly OwnerManager _ownerManager;
        private readonly ILogger<EditModel> _logger;

        public EditModel(LinkManager linkManager, OwnerManager ownerManager, ILogger<EditModel> logger)
        {
            _linkManager = linkManager ?? throw new ArgumentNullException(nameof(linkManager));
            _ownerManager = ownerManager ?? throw new ArgumentNullException(nameof(ownerManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [BindProperty] public InputModel Input { get; set; } = new();

        // pro karty/info v UI
        public string? ShortUrlPreview { get; private set; }
        public long Clicks { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? LastAccessAtUtc { get; private set; }
        public DateTime? DeletedAtUtc { get; private set; }

        // --- Status zprávy ---
        [TempData] public string? SuccessMessage { get; set; }
        [TempData] public string? FailureMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var sub = await _ownerManager.EnsureOwnerAsync(User);

            var link = await _linkManager.GetByIdAsync(id, includeRelated: false);
            if (link is null || link.OwnerSub != sub)
            {
                FailureMessage = "Odkaz nenalezen.";
                return RedirectToPage("./Index");
            }

            MapToInput(link);
            BuildPreview();
            MapStats(link);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            // validace rozsahu platnosti
            if (Input.ActiveFromUtc is not null && Input.ActiveToUtc is not null &&
                Input.ActiveToUtc < Input.ActiveFromUtc)
            {
                ModelState.AddModelError(nameof(Input.ActiveToUtc), "Konec platnosti nesmí být døív než zaèátek.");
            }

            if (!ModelState.IsValid)
            {
                BuildPreview();
                return Page();
            }

            try
            {
                var sub = await _ownerManager.EnsureOwnerAsync(User);
                var link = await _linkManager.GetByIdAsync(id, includeRelated: false);

                if (link is null || link.OwnerSub != sub)
                {
                    FailureMessage = "Odkaz nenalezen.";
                    return RedirectToPage("./Index");
                }

                // Concurrency token (optimistic concurrency)
                if (!string.IsNullOrWhiteSpace(Input.RowVersion))
                    link.RowVersion = Convert.FromBase64String(Input.RowVersion);

                // povolené zmìny (bez OwnerSub a bez Domény)
                link.Code = Input.Code!.Trim();
                link.TargetUrl = Input.TargetUrl!;
                link.Title = string.IsNullOrWhiteSpace(Input.Title) ? null : Input.Title;
                link.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note;
                link.ActiveFromUtc = Input.ActiveFromUtc;
                link.ActiveToUtc = Input.ActiveToUtc;
                link.IsEnabled = Input.IsEnabled;

                var updated = await _linkManager.UpdateAsync(link);
                if (updated is null)
                {
                    ModelState.AddModelError(string.Empty, "Zmìny se nepodaøilo uložit.");
                    BuildPreview();
                    MapStats(link);
                    return Page();
                }

                SuccessMessage = "Zmìny byly uloženy.";
                return RedirectToPage("./Index");
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty,
                    "Záznam byl mezitím zmìnìn nìkým jiným. Naètìte stránku znovu a zkuste to ještì jednou.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("existuje", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(Input.Code), "Krátký kód už existuje. Zvolte jiný.");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Chyba databáze pøi ukládání odkazu.");
                ModelState.AddModelError(string.Empty, "Chyba databáze pøi ukládání odkazu.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Neoèekávaná chyba pøi ukládání odkazu.");
                ModelState.AddModelError(string.Empty, "Neoèekávaná chyba pøi ukládání odkazu.");
            }

            BuildPreview();
            return Page();
        }

        private void MapToInput(Link link)
        {
            Input = new InputModel
            {
                Id = link.Id,
                Code = link.Code,
                TargetUrl = link.TargetUrl,
                Title = link.Title,
                Note = link.Note,
                ActiveFromUtc = link.ActiveFromUtc,
                ActiveToUtc = link.ActiveToUtc,
                IsEnabled = link.IsEnabled,
                RowVersion = link.RowVersion is null ? null : Convert.ToBase64String(link.RowVersion)
            };
        }

        private void MapStats(Link link)
        {
            Clicks = link.Clicks;
            CreatedAt = link.CreatedAt;
            LastAccessAtUtc = link.LastAccessAt;
            DeletedAtUtc = link.DeletedAt;
        }

        private void BuildPreview()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var code = string.IsNullOrWhiteSpace(Input.Code) ? "••••••" : Input.Code!.Trim();

            try
            {
                ShortUrlPreview = _linkManager.GenerateShortUrl(null, code, baseUrl);
            }
            catch
            {
                ShortUrlPreview = $"{baseUrl}/{code}";
            }
        }

        public class InputModel
        {
            [HiddenInput] public int Id { get; set; }

            [Required, StringLength(16), Display(Name = "Krátký kód")]
            public string? Code { get; set; }

            [Required, StringLength(2048), Url, Display(Name = "Cílová adresa (URL)")]
            public string? TargetUrl { get; set; }

            [StringLength(256), Display(Name = "Titulek")]
            public string? Title { get; set; }

            [StringLength(1024), Display(Name = "Poznámka")]
            public string? Note { get; set; }

            [Display(Name = "Aktivní od (UTC)")]
            public DateTime? ActiveFromUtc { get; set; }

            [Display(Name = "Aktivní do (UTC)")]
            public DateTime? ActiveToUtc { get; set; }

            [Display(Name = "Povolený odkaz")]
            public bool IsEnabled { get; set; } = true;

            // Concurrency token jako base64 string (snazší binding ve formuláøi)
            public string? RowVersion { get; set; }
        }
    }
}
