using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace PslibUrlShortener.Model
{
    [Index(nameof(Email))]
    public class Owner
    {
        [Key, StringLength(128)]
        public required string Sub { get; set; }

        [StringLength(256)]
        public string? DisplayName { get; set; }

        [StringLength(256)]
        public string? Email { get; set; }

        [StringLength(128)]
        public string? GivenName { get; set; }

        [StringLength(128)]
        public string? FamilyName { get; set; }

        [Column(TypeName = "datetime2(0)")]
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}