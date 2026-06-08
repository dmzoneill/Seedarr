using System.Collections.Generic;
using DryIoc;

namespace NzbDrone.Common;

public interface IServiceFactory
{
    T Build<T>() where T : class;
    IEnumerable<T> BuildAll<T>() where T : class;
}

public class ServiceFactory : IServiceFactory
{
    private readonly IResolver _resolver;

    public ServiceFactory(IResolver resolver)
    {
        _resolver = resolver;
    }

    public T Build<T>() where T : class
    {
        return _resolver.Resolve<T>();
    }

    public IEnumerable<T> BuildAll<T>() where T : class
    {
        return _resolver.ResolveMany<T>();
    }
}
