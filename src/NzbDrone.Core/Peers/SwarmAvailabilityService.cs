using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public interface ISwarmAvailabilityService
{
    double CalculateAvailability(Torrent torrent, IEnumerable<PeerConnection> peers, bool[] verifiedPieces = null);
    double CalculateAvailability(int pieceCount, bool[] localPieces, IEnumerable<PeerConnection> peers);
    double CalculateAvailability(int pieceCount, bool[] localPieces, IReadOnlyList<int> peerPieceFrequencies);
    (double Availability, bool IsExtinct, int ExtinctPieceCount) CalculateSwarmAvailability(
        Torrent torrent,
        IEnumerable<PeerConnection> peers,
        bool[] verifiedPieces = null);
    double CalculateCumulativeAvailability(IEnumerable<PeerConnection> peers, int pieceCount);
}

public class SwarmAvailabilityService : ISwarmAvailabilityService
{
    public static double CalculateAvailability(int pieceCount, bool[] localPieces, IEnumerable<PeerConnection> peers)
    {
        if (pieceCount <= 0)
        {
            return 0.0;
        }

        var peerList = peers?.Where(p => p != null).ToList() ?? new List<PeerConnection>();
        var availability = new int[pieceCount];

        for (var i = 0; i < pieceCount; i++)
        {
            if (localPieces != null && i < localPieces.Length && localPieces[i])
            {
                availability[i] = 1;
            }
        }

        foreach (var peer in peerList)
        {
            if (peer.IsSeed || peer.Progress >= 1.0)
            {
                for (var i = 0; i < pieceCount; i++)
                {
                    availability[i]++;
                }
            }
            else if (peer.PeerPieces != null && peer.PeerPieces.Length > 0)
            {
                var len = Math.Min(pieceCount, peer.PeerPieces.Length);
                for (var i = 0; i < len; i++)
                {
                    if (peer.PeerPieces[i])
                    {
                        availability[i]++;
                    }
                }
            }
        }

        return ComputeCanonicalAvailability(availability, pieceCount);
    }

    public static double CalculateAvailability(int pieceCount, bool[] localPieces, IReadOnlyList<int> peerPieceFrequencies)
    {
        if (pieceCount <= 0)
        {
            return 0.0;
        }

        var availability = new int[pieceCount];
        for (var i = 0; i < pieceCount; i++)
        {
            var local = localPieces != null && i < localPieces.Length && localPieces[i] ? 1 : 0;
            var peer = peerPieceFrequencies != null && i < peerPieceFrequencies.Count ? peerPieceFrequencies[i] : 0;
            availability[i] = local + peer;
        }

        return ComputeCanonicalAvailability(availability, pieceCount);
    }

    public static double CalculateAvailability(Torrent torrent, IEnumerable<PeerConnection> peers, bool[] verifiedPieces = null)
    {
        if (torrent == null)
        {
            return 0.0;
        }

        var (avail, _, _) = CalculateSwarmAvailability(torrent, peers, verifiedPieces);
        return avail;
    }

    public static (double Availability, bool IsExtinct, int ExtinctPieceCount) CalculateSwarmAvailability(
        Torrent torrent,
        IEnumerable<PeerConnection> peers,
        bool[] verifiedPieces = null)
    {
        if (torrent == null)
        {
            return (0.0, false, 0);
        }

        var pieceCount = torrent.PieceCount;
        var peerList = peers?.Where(p => p != null).ToList() ?? new List<PeerConnection>();
        var hasSeed = peerList.Any(p => p.IsSeed || p.Progress >= 1.0);

        if (pieceCount <= 0)
        {
            return (0.0, false, 0);
        }

        var localPieces = new bool[pieceCount];
        var localCount = 0;
        for (var i = 0; i < pieceCount; i++)
        {
            if (LocalHasPiece(torrent, verifiedPieces, i))
            {
                localPieces[i] = true;
                localCount++;
            }
        }

        var copyCount = new int[pieceCount];
        for (var i = 0; i < pieceCount; i++)
        {
            if (localPieces[i])
            {
                copyCount[i] = 1;
            }
        }

        foreach (var peer in peerList)
        {
            if (peer.IsSeed || peer.Progress >= 1.0)
            {
                for (var i = 0; i < pieceCount; i++)
                {
                    copyCount[i]++;
                }
            }
            else if (peer.PeerPieces != null && peer.PeerPieces.Length > 0)
            {
                var len = Math.Min(pieceCount, peer.PeerPieces.Length);
                for (var i = 0; i < len; i++)
                {
                    if (peer.PeerPieces[i])
                    {
                        copyCount[i]++;
                    }
                }
            }
        }

        var availability = ComputeCanonicalAvailability(copyCount, pieceCount);

        if (localCount == pieceCount || torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding)
        {
            return (Math.Max(1.0, availability), false, 0);
        }

        if (hasSeed)
        {
            return (availability, false, 0);
        }

        var missingCount = pieceCount - localCount;
        var extinctPieceCount = 0;
        for (var i = 0; i < pieceCount; i++)
        {
            if (!localPieces[i] && copyCount[i] == 0)
            {
                extinctPieceCount++;
            }
        }

        var allMissingExtinct = missingCount > 0 && extinctPieceCount == missingCount;
        return (availability, allMissingExtinct, extinctPieceCount);
    }

    public static double CalculateCumulativeAvailability(IEnumerable<PeerConnection> peers, int pieceCount)
    {
        if (peers == null || pieceCount <= 0)
        {
            return 0.0;
        }

        var peerList = peers.Where(p => p != null).ToList();
        if (peerList.Count == 0)
        {
            return 0.0;
        }

        if (peerList.Any(p => p.IsSeed || p.Progress >= 1.0))
        {
            var seedCount = peerList.Count(p => p.IsSeed || p.Progress >= 1.0);
            var availability = new int[pieceCount];
            for (var i = 0; i < pieceCount; i++)
            {
                availability[i] = seedCount;
            }

            foreach (var peer in peerList.Where(p => !p.IsSeed && p.Progress < 1.0))
            {
                if (peer.PeerPieces != null && peer.PeerPieces.Length > 0)
                {
                    var len = Math.Min(pieceCount, peer.PeerPieces.Length);
                    for (var i = 0; i < len; i++)
                    {
                        if (peer.PeerPieces[i])
                        {
                            availability[i]++;
                        }
                    }
                }
            }

            return ComputeCanonicalAvailability(availability, pieceCount);
        }

        var copyCount = new int[pieceCount];
        var hasAnyBitfield = false;

        foreach (var peer in peerList)
        {
            if (peer.PeerPieces != null && peer.PeerPieces.Length > 0)
            {
                hasAnyBitfield = true;
                var len = Math.Min(pieceCount, peer.PeerPieces.Length);
                for (var i = 0; i < len; i++)
                {
                    if (peer.PeerPieces[i])
                    {
                        copyCount[i]++;
                    }
                }
            }
        }

        if (hasAnyBitfield)
        {
            return ComputeCanonicalAvailability(copyCount, pieceCount);
        }

        var totalProgress = peerList.Sum(p => Math.Clamp(p.Progress, 0.0, 1.0));
        return Math.Min(1.0, totalProgress);
    }

    private static double ComputeCanonicalAvailability(int[] availability, int pieceCount)
    {
        if (pieceCount <= 0 || availability == null || availability.Length == 0)
        {
            return 0.0;
        }

        var minA = int.MaxValue;
        for (var i = 0; i < pieceCount; i++)
        {
            if (availability[i] < minA)
            {
                minA = availability[i];
            }
        }

        var countAboveMin = 0;
        for (var i = 0; i < pieceCount; i++)
        {
            if (availability[i] > minA)
            {
                countAboveMin++;
            }
        }

        return minA + ((double)countAboveMin / pieceCount);
    }

    private static bool LocalHasPiece(Torrent torrent, bool[] verifiedPieces, int pieceIndex)
    {
        if (verifiedPieces != null && pieceIndex >= 0 && pieceIndex < verifiedPieces.Length)
        {
            return verifiedPieces[pieceIndex];
        }

        if (torrent == null || torrent.PieceCount <= 0)
        {
            return false;
        }

        if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding)
        {
            return true;
        }

        if (torrent.Progress > 0)
        {
            var verifiedCount = (int)Math.Round(torrent.Progress * torrent.PieceCount);
            return pieceIndex < verifiedCount;
        }

        return false;
    }

    double ISwarmAvailabilityService.CalculateAvailability(Torrent torrent, IEnumerable<PeerConnection> peers, bool[] verifiedPieces)
        => CalculateAvailability(torrent, peers, verifiedPieces);

    double ISwarmAvailabilityService.CalculateAvailability(int pieceCount, bool[] localPieces, IEnumerable<PeerConnection> peers)
        => CalculateAvailability(pieceCount, localPieces, peers);

    double ISwarmAvailabilityService.CalculateAvailability(int pieceCount, bool[] localPieces, IReadOnlyList<int> peerPieceFrequencies)
        => CalculateAvailability(pieceCount, localPieces, peerPieceFrequencies);

    (double Availability, bool IsExtinct, int ExtinctPieceCount) ISwarmAvailabilityService.CalculateSwarmAvailability(
        Torrent torrent,
        IEnumerable<PeerConnection> peers,
        bool[] verifiedPieces)
        => CalculateSwarmAvailability(torrent, peers, verifiedPieces);

    double ISwarmAvailabilityService.CalculateCumulativeAvailability(IEnumerable<PeerConnection> peers, int pieceCount)
        => CalculateCumulativeAvailability(peers, pieceCount);
}
