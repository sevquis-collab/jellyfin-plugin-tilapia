using System.Net;
using System.Text;
using Xunit;

namespace Jellyfin.Plugin.Podcasts.Tests;

public sealed class FeedParserTests
{
    [Fact]
    public void ParsesRssPodcastMetadataAndEnclosure()
    {
        const string Xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd">
              <channel>
                <title>Example Podcast</title>
                <description>An example feed.</description>
                <itunes:image href="https://cdn.example.test/cover.jpg" />
                <item>
                  <guid>episode-1</guid>
                  <title>First episode</title>
                  <description>Hello listeners.</description>
                  <pubDate>Thu, 13 Aug 2026 12:00:00 +0000</pubDate>
                  <itunes:duration>01:02:03</itunes:duration>
                  <enclosure url="https://cdn.example.test/episode-1.mp3" type="audio/mpeg" length="12345" />
                </item>
              </channel>
            </rss>
            """;

        var feed = PodcastFeedClient.Parse(Encoding.UTF8.GetBytes(Xml));

        Assert.Equal("Example Podcast", feed.Title);
        Assert.Equal("An example feed.", feed.Description);
        Assert.Equal("https://cdn.example.test/cover.jpg", feed.ImageUrl);
        var episode = Assert.Single(feed.Episodes);
        Assert.Equal("First episode", episode.Title);
        Assert.Equal("https://cdn.example.test/episode-1.mp3", episode.AudioUrl);
        Assert.Equal("audio/mpeg", episode.MimeType);
        Assert.Equal(12345, episode.Length);
        Assert.Equal(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3), TimeSpan.FromTicks(episode.RuntimeTicks!.Value));
    }

    [Fact]
    public void ParsesAtomEnclosureAndIgnoresTextOnlyEntries()
    {
        const string Xml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Atom Podcast</title>
              <subtitle>Atom description</subtitle>
              <link rel="icon" href="https://cdn.example.test/unused.png" />
              <entry>
                <id>text-only</id>
                <title>Text post</title>
              </entry>
              <entry>
                <id>audio-entry</id>
                <title>Audio entry</title>
                <updated>2026-08-13T12:00:00Z</updated>
                <link rel="enclosure" href="https://cdn.example.test/audio.ogg" type="audio/ogg" length="99" />
              </entry>
            </feed>
            """;

        var feed = PodcastFeedClient.Parse(Encoding.UTF8.GetBytes(Xml));

        Assert.Equal("Atom Podcast", feed.Title);
        var episode = Assert.Single(feed.Episodes);
        Assert.Equal("Audio entry", episode.Title);
        Assert.Equal("https://cdn.example.test/audio.ogg", episode.AudioUrl);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    public void RejectsNonPublicAddresses(string value)
        => Assert.True(PodcastFeedClient.IsPrivateAddress(IPAddress.Parse(value)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void AllowsPublicAddresses(string value)
        => Assert.False(PodcastFeedClient.IsPrivateAddress(IPAddress.Parse(value)));
}
