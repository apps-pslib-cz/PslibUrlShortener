using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PslibUrlShortener.Data;
using PslibUrlShortener.Model;
using PslibUrlShortener.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace PslibUrlShortener.Areas.Admin.Pages
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

        // --- Status zprávy ---
        [TempData]
        public string? SuccessMessage { get; set; }
        [TempData]
        public string? FailureMessage { get; set; }
        public void OnGet()
        {
            // Initial GET request, just display the form
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }
            try
            {
                var userName = User.Identity?.IsAuthenticated == true ? User.Identity?.Name ?? "unknown" : "anonymous";
                var link = await _linkManager.CreateAsync(
                    new Link
                    {
                        TargetUrl = Input.TargetUrl,
                        Code = Input.CustomCode ?? _linkManager.GenerateUniqueCode(),
                        ActiveFromUtc = Input.ActiveFromUtc,
                        ActiveToUtc = Input.ActiveToUtc,
                        IsEnabled = Input.IsEnabled,
                        OwnerSub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown"
                    }
                );
                SuccessMessage = $"Short link created successfully: {link.Code}";
                return RedirectToPage("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating short link");
                FailureMessage = $"Error creating short link: {ex.Message}";
                return Page();
            }
        }
    }

    public class InputModel
    {
        [Required]
        [Url]
        [Display(Name = "Target URL")]
        public string TargetUrl { get; set; } = string.Empty;
        [Display(Name = "Custom Short Code")]
        [RegularExpression("^[a-zA-Z0-9_-]{3,20}$", ErrorMessage = "Short code must be 3-20 characters long and can only contain letters, numbers, underscores, and hyphens.")]
        public string? CustomCode { get; set; }
        [Display(Name = "Active From (UTC)")]
        public DateTime? ActiveFromUtc { get; set; }
        [Display(Name = "Active To (UTC)")]
        public DateTime? ActiveToUtc { get; set; }
        [Display(Name = "Is Enabled")]
        public bool IsEnabled { get; set; } = true;
    }
}