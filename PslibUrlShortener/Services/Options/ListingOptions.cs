namespace PslibUrlShortener.Services.Options
{
    public class ListingOptions
    {
        public int DefaultPageSize { get; set; }
        public ICollection<int> PageSizes { get; set; } = new List<int>();
    }
}
