using System.ComponentModel.DataAnnotations;

namespace PslibUrlShortener.Model
{
    public class ReservedCode
    {
        [Key, StringLength(32)]
        public required string Code { get; set; }

        [StringLength(256)]
        public string? Reason { get; set; }
    }
}