using System.Collections.Generic;

namespace NzbDrone.Core.ThingiProvider;

public interface IProviderFactory<TProvider, TProviderDefinition>
    where TProvider : IProvider
    where TProviderDefinition : ProviderDefinition, new()
{
    List<TProviderDefinition> All();
    TProviderDefinition Get(int id);
    TProviderDefinition Create(TProviderDefinition definition);
    void Update(TProviderDefinition definition);
    void Delete(int id);
    List<TProvider> GetAvailableProviders();
}
