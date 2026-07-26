namespace Application.Common.Models
{
    /// <summary>
    /// Bound from the "Pagination" section of appsettings.json.
    /// </summary>
    public class PaginationOptions
    {
        public const string SectionName = "Pagination";

        public int MaxPageSize { get; set; } = 100;
    }
}
