using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Podcasts;

public sealed class Plugin : BasePlugin<BasePluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer) { }

    public override string Name => "Podcasts";
    public override string Description => "Per-user public podcasts, with provider-dependent private RSS support, isolated from Jellyfin libraries.";
    public override Guid Id => Guid.Parse("f238df10-b186-48b8-9993-cf3bc7c7d988");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "Podcasts",
            DisplayName = "Podcasts",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.podcasts.html",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "podcasts"
        };
    }
}
