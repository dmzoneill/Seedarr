using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags;

public interface ITagRepository : IBasicRepository<Tag>
{
}

public class TagRepository : BasicRepository<Tag>, ITagRepository
{
    public TagRepository(IDatabase database)
        : base(database)
    {
    }
}
