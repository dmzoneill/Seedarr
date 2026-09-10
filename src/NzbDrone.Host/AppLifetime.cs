using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using Seedarr.Http.Authentication;

namespace NzbDrone.Host;

public class AppLifetime : IHostedService
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IDynamicAuthSchemeManager _dynamicAuthManager;
    private readonly Logger _logger;

    public AppLifetime(IEventAggregator eventAggregator, IDynamicAuthSchemeManager dynamicAuthManager = null)
    {
        _eventAggregator = eventAggregator;
        _dynamicAuthManager = dynamicAuthManager;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_dynamicAuthManager != null)
        {
            try
            {
                await _dynamicAuthManager.InitializeConfiguredProvidersAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error initializing dynamic authentication providers on startup");
            }
        }

        _logger.Info("Seedarr application started");
        _eventAggregator.PublishEvent(new ApplicationStartedEvent());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Seedarr application stopping");
        _eventAggregator.PublishEvent(new ApplicationShutdownRequested());
        return Task.CompletedTask;
    }
}
