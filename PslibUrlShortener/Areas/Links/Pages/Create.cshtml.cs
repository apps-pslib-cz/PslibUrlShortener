using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace PslibUrlShortener.Areas.Links.Pages
{
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(ApplicationDbContext db, ILogger<CreateModel> logger)
        {
            _db = db;
            _logger = logger;
        }

        [BindProperty, Url, Required, StringLength(2048)]
        public string TargetUrl { get; set; } = string.Empty;

        [BindProperty, StringLength(16)]
        public string? CustomCode { get; set; }

        [BindProperty]
        public bool IsEnabled { get; set; } = true;

        [BindProperty]
        public string? Title { get; set; }

        [BindProperty]
        public string? Note { get; set; }

        // Lokální èasy (Europe/Prague); ukládáme jako UTC
        [BindProperty]
        public DateTime? ActiveFromLocal { get; set; }

        [BindProperty]
        public DateTime? ActiveToLocal { get; set; }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!TryNormalizeUrl(TargetUrl, out var normalized))
            {
                ModelState.AddModelError(nameof(TargetUrl), "Neplatná URL. Povolené schéma je http/https.");
                return Page();
            }

            var sub = User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(sub)) return Forbid();

            var code = string.IsNullOrWhiteSpace(CustomCode)
                ? GenerateCode(7)
                : CustomCode!.Trim().ToLowerInvariant();

            if (!IsCodeSafe(code))
            {
                ModelState.AddModelError(nameof(CustomCode), "Kód obsahuje nepovolené znaky. Povolené jsou a–z, 0–9.");
                return Page();
            }

            // kolize s rezervovanými kódy
            var reserved = await _db.ReservedCodes.AnyAsync(r => r.Code == code);
            if (reserved)
            {
                ModelState.AddModelError(nameof(CustomCode), "Kód je rezervován.");
                return Page();
            }

            // kolize s existujícími (nesmazanými) odkazy
            var exists = await _db.Links.AnyAsync(l => l.DeletedAt == null && l.Code == code);
            if (exists)
            {
                ModelState.AddModelError(nameof(CustomCode), "Kód už existuje.");
                return Page();
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

            var entity = new Link
            {
                Code = code,
                TargetUrl = normalized,
                IsEnabled = IsEnabled,
                OwnerSub = sub,
                OwnerName = User.FindFirst("name")?.Value ?? User.FindFirst("email")?.Value,
                Title = Title,
                Note = Note,
                CreatedAt = DateTime.UtcNow,
                ActiveFromUtc = ActiveFromLocal.HasValue
                    ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ActiveFromLocal.Value, DateTimeKind.Unspecified), tz)
                    : null,
                ActiveToUtc = ActiveToLocal.HasValue
                    ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ActiveToLocal.Value, DateTimeKind.Unspecified), tz)
                    : null
            };

            if (entity.ActiveFromUtc.HasValue && entity.ActiveToUtc.HasValue
                && entity.ActiveToUtc <= entity.ActiveFromUtc)
            {
                ModelState.AddModelError(nameof(ActiveToLocal), "Èas do musí být pozdìji než èas od.");
                return Page();
            }

            _db.Links.Add(entity);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Odkaz /{entity.Code} vytvoøen.";
            return RedirectToPage("Index");
        }

        private static bool TryNormalizeUrl(string input, out string normalized)
        {
            normalized = input.Trim();
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme is not ("http" or "https")) return false;
            normalized = uri.ToString();
            return true;
        }

        private static bool IsCodeSafe(string code)
        {
            // a-z + 0-9
            return code.All(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'));
        }

        private static string GenerateCode(int length)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            Span<byte> buf = stackalloc byte[length];
            rng.GetBytes(buf);
            var chars = new char[length];
            for (int i = 0; i < length; i++)
                chars[i] = alphabet[buf[i] % alphabet.Length];
            return new string(chars);
        }
    }
}