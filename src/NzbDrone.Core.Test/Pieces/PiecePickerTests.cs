using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Pieces;

namespace NzbDrone.Core.Test.Pieces;

[TestFixture]
public class PiecePickerTests
{
    [Test]
    public void RarestFirstPicker_selects_lowest_frequency_piece()
    {
        var picker = new RarestFirstPicker();

        var myPieces = new BitArray(4, false);
        var peerPieces = new BitArray(4, true);
        var availability = new[] { 5, 2, 8, 3 };

        var picked = picker.PickPiece(myPieces, peerPieces, availability, sequential: false);
        Assert.That(picked, Is.EqualTo(1));
    }

    [Test]
    public void SequentialPicker_selects_ascending_pieces()
    {
        var picker = new SequentialPicker();

        var myPieces = new BitArray(4, false);
        var peerPieces = new BitArray(4, true);
        var availability = new[] { 10, 1, 10, 1 };

        var picked0 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);
        Assert.That(picked0, Is.EqualTo(0));

        myPieces[0] = true;
        var picked1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);
        Assert.That(picked1, Is.EqualTo(1));
    }

    [Test]
    public void SwarmAvailabilityService_in_pieces_namespace_computes_availability()
    {
        var localPieces = new[] { true, true, false, false };
        var frequencies = new[] { 1, 0, 1, 1 };

        var avail = SwarmAvailabilityService.CalculateAvailability(4, localPieces, frequencies);
        // A = [2, 1, 1, 1]. min = 1, count > 1 = 1 (piece 0).
        // Availability = 1 + 1/4 = 1.25.
        Assert.That(avail, Is.EqualTo(1.25));
    }
}
