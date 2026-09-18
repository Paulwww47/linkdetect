namespace LinkDetect.Tests;

public sealed class LinkExtractorTests
{
    [Fact]
    public void ExtractsDouyinLinkFromShareText()
    {
        const string text = "8.23 复制打开抖音，看看【杰后余生的图文作品】# 这才是真正的爬山玩水 # 大家下午好，五一没事... https://v.douyin.com/UXk5NBFWo-U/ :7pm 07/27 a@a.NJ yGi:/";

        var links = LinkExtractor.Extract(text);

        Assert.Equal(["https://v.douyin.com/UXk5NBFWo-U/"], links);
    }

    [Fact]
    public void ExtractsXiaohongshuLinkFromShareText()
    {
        const string text = "@一个桃脯 在小红书收获了22.5K次赞与收藏，查看Ta的主页>> https://xhslink.com/m/139cW7KHkER";

        var links = LinkExtractor.Extract(text);

        Assert.Equal(["https://xhslink.com/m/139cW7KHkER"], links);
    }

    [Fact]
    public void ExtractsMultipleLinksInOrderAndRemovesDuplicates()
    {
        const string text = "https://example.com/a\nhttps://example.com/b https://example.com/a";

        var links = LinkExtractor.Extract(text);

        Assert.Equal(["https://example.com/a", "https://example.com/b"], links);
    }

    [Theory]
    [InlineData("链接：https://example.com/path。", "https://example.com/path")]
    [InlineData("(https://example.com/path)", "https://example.com/path")]
    [InlineData("（https://example.com/path）", "https://example.com/path")]
    [InlineData("“https://example.com/path?x=1&y=2”", "https://example.com/path?x=1&y=2")]
    [InlineData("https://example.com/a_(b)", "https://example.com/a_(b)")]
    public void TrimsCommonTrailingPunctuation(string text, string expected)
    {
        Assert.Equal([expected], LinkExtractor.Extract(text));
    }

    [Fact]
    public void PreservesPortsQueriesFragmentsAndEscapedCharacters()
    {
        const string text = "https://example.com:8443/a%20b?q=hello%20world#part-2";

        Assert.Equal([text], LinkExtractor.Extract(text));
    }

    [Fact]
    public void PreservesOriginalDisplayTextWhileDeduplicatingCanonicalUri()
    {
        const string text = "HTTPS://EXAMPLE.COM/Path https://example.com/Path";

        Assert.Equal(["HTTPS://EXAMPLE.COM/Path"], LinkExtractor.Extract(text));
    }

    [Theory]
    [InlineData("普通文本")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///C:/temp/test.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    public void RejectsUnsupportedOrInvalidContent(string text)
    {
        Assert.Empty(LinkExtractor.Extract(text));
    }
}
