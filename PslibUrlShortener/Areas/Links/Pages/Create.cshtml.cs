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
    }
}