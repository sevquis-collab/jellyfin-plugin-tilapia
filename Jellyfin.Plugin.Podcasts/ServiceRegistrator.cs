using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Podcasts;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<PodcastStore>();
        services.AddSingleton<PodcastFeedClient>();
        services.AddSingleton<PodcastDirectoryClient>();
        services.AddSingleton<IChannel, PodcastChannel>();
        services.AddSingleton<MediaBrowser.Model.Tasks.IScheduledTask, PrivateFeedRefreshTask>();
    }
}
