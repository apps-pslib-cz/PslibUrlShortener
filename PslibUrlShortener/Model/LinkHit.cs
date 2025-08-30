using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PslibUrlShortener.Model
{
    [Index(nameof(LinkId))]
    [Index(nameof(AtUtc))]
    public class LinkHit
    {
        public long Id { get; set; }

        public int LinkId { get; set; }

        public Link Link { get; set; } = default!;

        [Column(TypeName = "datetime2(0)")]
        public DateTime AtUtc { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "nvarchar(2048)")]
        public string? Referer { get; set; }

        [Column(TypeName = "nvarchar(512)")]
        public string? UserAgent { get; set; }

        [MaxLength(32)] // 32 bytů pro SHA-256
        public byte[]? RemoteIpHash { get; set; }

        public bool IsBot { get; set; }
    }
}