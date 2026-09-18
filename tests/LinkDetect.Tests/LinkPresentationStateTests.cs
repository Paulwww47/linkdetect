namespace LinkDetect.Tests;

public sealed class LinkPresentationStateTests
{
    [Fact]
    public void ReplaceMakesNewListVisibleAndDiscardsOldList()
    {
        var state = new LinkPresentationState();
        state.Replace(["https://old.example"]);

        state.Replace(["https://new.example/1", "https://new.example/2"]);

        Assert.True(state.IsVisible);
        Assert.Equal(["https://new.example/1", "https://new.example/2"], state.Links);
    }

    [Fact]
    public void RemovingLinksHidesStateAfterLastLink()
    {
        var state = new LinkPresentationState();
        state.Replace(["https://example.com/1", "https://example.com/2"]);

        Assert.True(state.Remove("https://example.com/1"));
        Assert.True(state.IsVisible);
        Assert.True(state.Remove("https://example.com/2"));
        Assert.False(state.IsVisible);
    }

    [Fact]
    public void ClearHidesCurrentList()
    {
        var state = new LinkPresentationState();
        state.Replace(["https://example.com"]);

        state.Clear();

        Assert.False(state.IsVisible);
        Assert.Empty(state.Links);
    }
}
