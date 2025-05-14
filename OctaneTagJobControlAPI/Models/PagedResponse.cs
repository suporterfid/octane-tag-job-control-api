namespace OctaneTagJobControlAPI.Models
{
    public class PaginationInfo
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
    }

    public class PagedTagDataResponse
    {
        public string JobId { get; set; }
        public List<TagReadData> Tags { get; set; }
        public PaginationInfo Pagination { get; set; }
        public TagSummary Summary { get; set; }
    }

    public class TagSummary
    {
        public int TotalReads { get; set; }
        public int UniqueTagCount { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}