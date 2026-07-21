using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common;

namespace NzbDrone.Core.ThingiProvider;

public abstract class ProviderFactory<TProvider, TProviderDefinition> : IProviderFactory<TProvider, TProviderDefinition>
    where TProvider : class, IProvider
    where TProviderDefinition : ProviderDefinition, new()
{
    private readonly IProviderRepository<TProviderDefinition> _providerRepository;
    private readonly IServiceFactory _serviceFactory;
    private readonly Logger _logger;

    protected ProviderFactory(
        IProviderRepository<TProviderDefinition> providerRepository,
        IServiceFactory serviceFactory)
    {
        _providerRepository = providerRepository;
        _serviceFactory = serviceFactory;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<TProviderDefinition> All()
    {
        return _providerRepository.All().ToList();
    }

    public TProviderDefinition Get(int id)
    {
        return _providerRepository.Get(id);
    }

    public TProviderDefinition Create(TProviderDefinition definition)
    {
        _logger.Info("Adding {0} provider '{1}'", typeof(TProvider).Name, definition.Name);
        return _providerRepository.Insert(definition);
    }

    public void Update(TProviderDefinition definition)
    {
        _logger.Info("Updating {0} provider '{1}'", typeof(TProvider).Name, definition.Name);
        _providerRepository.Update(definition);
    }

    public void Delete(int id)
    {
        _logger.Info("Removing {0} provider {1}", typeof(TProvider).Name, id);
        _providerRepository.Delete(id);
    }

    public List<TProvider> GetAvailableProviders()
    {
        var enabledImplementations = All()
            .Where(d => d.Enable)
            .Select(d => d.Implementation)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _serviceFactory.BuildAll<TProvider>()
            .Where(p => enabledImplementations.Contains(p.GetType().Name))
            .ToList();
    }
}
