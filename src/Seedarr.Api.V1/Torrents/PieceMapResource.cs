using System.Collections.Generic;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

public class PieceMapSpanResource
{
    public int Count { get; set; }

    public int State { get; set; }

    public PieceMapSpanResource()
    {
    }

    public PieceMapSpanResource(int count, int state)
    {
        Count = count;
        State = state;
    }
}

public class PieceMapResource : RestResource
{
    public int TorrentId { get; set; }

    public string InfoHash { get; set; }

    public int TotalPieces { get; set; }

    public long PieceLength { get; set; }

    public List<PieceMapSpanResource> Spans { get; set; } = new();

    public List<int[]> RleSpans { get; set; } = new();

    public int[] Rarity { get; set; }

    public List<int[]> RaritySpans { get; set; } = new();

    public static List<PieceMapSpanResource> CompressToSpans(IReadOnlyList<int> states)
    {
        var spans = new List<PieceMapSpanResource>();
        if (states == null || states.Count == 0)
        {
            return spans;
        }

        var currentState = states[0];
        var currentCount = 1;

        for (var i = 1; i < states.Count; i++)
        {
            if (states[i] == currentState)
            {
                currentCount++;
            }
            else
            {
                spans.Add(new PieceMapSpanResource(currentCount, currentState));
                currentState = states[i];
                currentCount = 1;
            }
        }

        spans.Add(new PieceMapSpanResource(currentCount, currentState));
        return spans;
    }

    public static List<int[]> CompressToRleTuples(IReadOnlyList<int> values)
    {
        var spans = new List<int[]>();
        if (values == null || values.Count == 0)
        {
            return spans;
        }

        var currentValue = values[0];
        var currentCount = 1;

        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] == currentValue)
            {
                currentCount++;
            }
            else
            {
                spans.Add(new[] { currentCount, currentValue });
                currentValue = values[i];
                currentCount = 1;
            }
        }

        spans.Add(new[] { currentCount, currentValue });
        return spans;
    }
}
