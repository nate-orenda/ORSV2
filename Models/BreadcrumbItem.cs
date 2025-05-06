namespace ORSV2.Models
{
    public class BreadcrumbItem
    {
        public string Title { get; set; } = string.Empty;
        public string? Url { get; set; } // null means it's the current page
    }
}
