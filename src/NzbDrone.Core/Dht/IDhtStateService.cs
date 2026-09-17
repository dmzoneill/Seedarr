using System.Collections.Generic;

namespace NzbDrone.Core.Dht;

public interface IDhtStateService
{
    void SaveRoutingTable(IEnumerable<DhtNode> nodes);
    void SaveRoutingTable(IEnumerable<Node> nodes);
    List<DhtNode> LoadRoutingTable();
    List<Node> LoadRoutingTableNodes();
}
