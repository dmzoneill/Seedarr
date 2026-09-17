using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class PiecePickerTest
{
    private PiecePicker _picker;

    [SetUp]
    public void SetUp()
    {
        _picker = new PiecePicker();
    }

    [Test]
    public void MarkPieceActive_and_Inactive_tracks_pieces()
    {
        const string hash = "picker-hash";
        _picker.MarkPieceActive(hash, 3);
        _picker.MarkPieceActive(hash, 5);

        Assert.That(_picker.IsPieceActive(hash, 3), Is.True);
        Assert.That(_picker.IsPieceActive(hash, 5), Is.True);
        Assert.That(_picker.IsPieceActive(hash, 4), Is.False);

        var active = _picker.GetActivePieces(hash);
        Assert.That(active, Is.EquivalentTo(new[] { 3, 5 }));

        _picker.MarkPieceInactive(hash, 3);
        Assert.That(_picker.IsPieceActive(hash, 3), Is.False);
        Assert.That(_picker.IsPieceActive(hash, 5), Is.True);
    }

    [Test]
    public void PickNextPiece_selects_first_missing_and_non_active_piece()
    {
        const string hash = "picker-hash-2";
        var verified = new[] { true, true, false, false, false };
        _picker.MarkPieceActive(hash, 2);

        var next = _picker.PickNextPiece(hash, 5, verified);
        Assert.That(next, Is.EqualTo(3));
    }
}
