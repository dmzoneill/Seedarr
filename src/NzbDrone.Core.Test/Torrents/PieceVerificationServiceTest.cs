using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class PieceVerificationServiceTest
{
    private IPieceStorage _pieceStorage;
    private IPiecePicker _piecePicker;
    private PieceVerificationService _service;

    [SetUp]
    public void SetUp()
    {
        _pieceStorage = Substitute.For<IPieceStorage>();
        _piecePicker = Substitute.For<IPiecePicker>();
        _service = new PieceVerificationService(_pieceStorage, _piecePicker);
    }

    [Test]
    public void VerifyPiece_succeeds_when_hash_matches()
    {
        const string hash = "valid-hash";
        var pieceData = Encoding.UTF8.GetBytes("Piece data content for verification testing");
        var expectedHash = SHA1.HashData(pieceData);

        var result = _service.VerifyPiece(hash, 2, pieceData, expectedHash);

        Assert.That(result, Is.True);
        _piecePicker.Received(1).MarkPieceInactive(hash, 2);
        _pieceStorage.Received(1).MarkPieceVerified(hash, 2, pieceData.Length);
        _pieceStorage.DidNotReceive().MarkPieceCorrupted(Arg.Any<string>(), Arg.Any<int>());
    }

    [Test]
    public void VerifyPiece_fails_when_hash_mismatches()
    {
        const string hash = "invalid-hash";
        var pieceData = Encoding.UTF8.GetBytes("Piece data content for verification testing");
        var corruptHash = new byte[20];

        var result = _service.VerifyPiece(hash, 3, pieceData, corruptHash);

        Assert.That(result, Is.False);
        _piecePicker.Received(1).MarkPieceInactive(hash, 3);
        _pieceStorage.Received(1).MarkPieceCorrupted(hash, 3);
        _pieceStorage.DidNotReceive().MarkPieceVerified(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long>());
    }
}
