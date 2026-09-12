using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Podcasts;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<PodcastStore>();
        serviceCollection.AddSingleton<PodcastFeedClient>();
        serviceCollection.AddSingleton<PodcastDirectoryClient>();
        serviceCollection.AddSingleton<IChannel, PodcastChannel>();
        serviceCollection.AddSingleton<MediaBrowser.Model.Tasks.IScheduledTask, PrivateFeedRefreshTask>();
    }
}
