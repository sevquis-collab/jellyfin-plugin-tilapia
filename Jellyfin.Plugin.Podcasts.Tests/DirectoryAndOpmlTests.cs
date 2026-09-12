using System.Text;
using System.Xml;
using Xunit;

namespace Jellyfin.Plugin.Podcasts.Tests;

public sealed class DirectoryAndOpmlTests
{
    [Fact]
    public async Task ParsesDirectoryResultsAndRejectsEntriesWithoutFeeds()
    {
        const string Json = """
            {
              "resultCount": 2,
              "results": [
                {
                  "collectionName": "Example Podcast",
                  "artistName": "Example Publisher",
                  "feedUrl": "https://feeds.example.test/show.xml",
                  "artworkUrl600": "https://images.example.test/show.jpg",
                  "collectionViewUrl": "https://podcasts.apple.com/example",
                  "trackCount": 42,
                  "primaryGenreName": "Technology"
                },
                {
                  "collectionName": "No public feed"
                }
              ]
            }
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));

        var result = Assert.Single(await PodcastDirectoryClient.ParseAsync(stream));

        Assert.Equal("Example Podcast", result.Title);
        Assert.Equal("Example Publisher", result.Publisher);
        Assert.Equal("https://feeds.example.test/show.xml", result.FeedUrl);
        Assert.Equal(42, result.EpisodeCount);
        Assert.Equal("Technology", result.Genre);
    }

    [Fact]
    public void ParsesNestedOpmlAndRemovesDuplicateFeeds()
    {
        const string Xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <opml version="2.0"><body>
              <outline text="Folder">
                <outline type="rss" text="One" xmlUrl="https://feeds.example.test/one.xml" />
                <outline type="rss" title="Duplicate" xmlUrl="https://feeds.example.test/one.xml" />
                <outline type="rss" title="Two" xmlUrl="https://feeds.example.test/two.xml" />
              </outline>
            </body></opml>
            """;

        var feeds = OpmlService.Parse(Xml);

        Assert.Equal(2, feeds.Count);
        Assert.Equal("One", feeds[0].Title);
        Assert.Equal("https://feeds.example.test/two.xml", feeds[1].FeedUrl);
    }

    [Fact]
    public void ExportedOpmlRoundTripsEscapedValues()
    {
        var xml = OpmlService.Export([new OpmlFeed("Fish & Chips", "https://feeds.example.test/show?a=1&b=2")]);

        var feed = Assert.Single(OpmlService.Parse(xml));

        Assert.Equal("Fish & Chips", feed.Title);
        Assert.Equal("https://feeds.example.test/show?a=1&b=2", feed.FeedUrl);
    }

    [Fact]
    public void OpmlProhibitsDocumentTypeDeclarations()
    {
        const string Xml = "<!DOCTYPE opml [<!ENTITY xxe SYSTEM \"file:///private.txt\">]><opml version=\"2.0\"><body /></opml>";

        Assert.Throws<XmlException>(() => OpmlService.Parse(Xml));
    }
}
