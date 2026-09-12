using System.Xml;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Podcasts;

public static class OpmlService
{
    private const int MaximumCharacters = 2 * 1024 * 1024;

    public static IReadOnlyList<OpmlFeed> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) throw new InvalidDataException("The OPML file is empty.");
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumCharacters
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        if (!string.Equals(document.Root?.Name.LocalName, "opml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected file is not an OPML document.");

        return document.Descendants()
            .Where(x => string.Equals(x.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase))
            .Select(x => new OpmlFeed(
                ((string?)x.Attribute("title") ?? (string?)x.Attribute("text"))?.Trim(),
                ((string?)x.Attribute("xmlUrl") ?? string.Empty).Trim()))
            .Where(x => !string.IsNullOrWhiteSpace(x.FeedUrl))
            .GroupBy(x => x.FeedUrl, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    public static string Export(IEnumerable<OpmlFeed> feeds)
    {
        var body = new XElement("body", feeds.Select(feed => new XElement("outline",
            new XAttribute("type", "rss"),
            new XAttribute("text", feed.Title ?? feed.FeedUrl),
            new XAttribute("title", feed.Title ?? feed.FeedUrl),
            new XAttribute("xmlUrl", feed.FeedUrl))));
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("opml", new XAttribute("version", "2.0"),
                new XElement("head", new XElement("title", "Tilapia podcast subscriptions")),
                body));
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
