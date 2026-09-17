using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Tags;

public interface IAutoTaggerService
{
    List<AutoTaggerRule> GetAllRules();
    AutoTaggerRule GetRule(int id);
    AutoTaggerRule AddRule(AutoTaggerRule rule);
    AutoTaggerRule UpdateRule(AutoTaggerRule rule);
    void DeleteRule(int id);
    void EvaluateTorrent(Torrent torrent);
    void EvaluateAll();
}
