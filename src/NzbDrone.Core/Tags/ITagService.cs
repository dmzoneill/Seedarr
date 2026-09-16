using System.Collections.Generic;

namespace NzbDrone.Core.Tags;

public interface ITagService
{
    List<Tag> GetAll();
    Tag Get(int id);
    Tag Add(Tag tag);
    Tag Update(Tag tag);
    void Delete(int id);
    List<int> SyncTagsFromLabels(IEnumerable<string> labels);
    List<string> GetLabelsForTagIds(IEnumerable<int> tagIds);
}
