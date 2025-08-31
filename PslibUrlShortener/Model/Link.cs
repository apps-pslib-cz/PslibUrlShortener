using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace PslibUrlShortener.Model
{
    [Index(nameof(Domain), nameof(Code), IsUnique = true)]
    [Index(nameof(OwnerSub))]
    public class Link
    {
        public int Id { get; set; }

        [StringLength(255)]
        public string? Domain { get; set; }

        [Required, StringLength(16)]
        public required string Code { get; set; }

        [Required, StringLength(2048)]
        public required string TargetUrl { get; set; }

        public bool IsEnabled { get; set; } = true;

        [Required, StringLength(128)]
        public required string OwnerSub { get; set; }

        [JsonIgnore]
        public Owner Owner { get; set; } = default!;

        [Column(TypeName = "datetime2(0)")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "datetime2(0)")]
        public DateTime? ActiveFromUtc { get; set; }

        [Column(TypeName = "datetime2(0)")]
        public DateTime? ActiveToUtc { get; set; }

        public long Clicks { get; set; } = 0;

        [Column(TypeName = "datetime2(0)")]
        public DateTime? LastAccessAt { get; set; }

        [StringLength(256)]
        public string? Title { get; set; }

        [StringLength(1024)]
        public string? Note { get; set; }

        [Column(TypeName = "datetime2(0)")]
        public DateTime? DeletedAt { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        [JsonIgnore]
        public ICollection<LinkHit>? Hits { get; set; }
    }
}