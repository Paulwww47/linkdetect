namespace LinkDetect;

public sealed class LinkPresentationState
{
    private readonly List<string> _links = [];

    public IReadOnlyList<string> Links => _links;

    public bool IsVisible => _links.Count > 0;

    public void Replace(IEnumerable<string> links)
    {
        ArgumentNullException.ThrowIfNull(links);
        _links.Clear();
        _links.AddRange(links);
    }

    public bool Remove(string link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return _links.Remove(link);
    }

    public void Clear() => _links.Clear();
}
