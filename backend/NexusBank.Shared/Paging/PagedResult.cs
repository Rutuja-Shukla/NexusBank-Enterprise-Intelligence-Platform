namespace NexusBank.Shared.Paging;

/// <summary>Standard paging envelope returned by every list endpoint.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;

    public static PagedResult<T> Empty(int page, int pageSize) => new(Array.Empty<T>(), page, pageSize, 0);
}

/// <summary>Query parameters shared by list endpoints. Values are clamped server-side.</summary>
public class PagedQuery
{
    public const int MaxPageSize = 200;
    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value is < 1 ? 20 : Math.Min(value, MaxPageSize);
    }

    public string? Search { get; set; }
    public string? Sort { get; set; }
    public int Skip => (Page - 1) * PageSize;
}
