using NUnit.Framework;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PeerMessageTest
{
    [Test]
    public void Length_should_be_1_when_no_payload()
    {
        var message = new PeerMessage { Type = PeerMessageType.Choke };

        Assert.That(message.Length, Is.EqualTo(1));
    }

    [Test]
    public void Length_should_include_payload_length_plus_1()
    {
        var message = new PeerMessage
        {
            Type = PeerMessageType.Piece,
            Payload = new byte[100]
        };

        Assert.That(message.Length, Is.EqualTo(101));
    }

    [Test]
    public void Length_should_reflect_payload_array_length_when_PayloadLength_is_negative()
    {
        var message = new PeerMessage
        {
            Payload = new byte[42]
        };

        Assert.That(message.Length, Is.EqualTo(43));
    }

    [Test]
    public void Length_should_use_explicit_PayloadLength_when_set_to_non_negative()
    {
        var message = new PeerMessage
        {
            Payload = new byte[100],
            PayloadLength = 50
        };

        Assert.That(message.Length, Is.EqualTo(51));
    }

    [Test]
    public void Length_should_be_1_when_payload_is_null_and_PayloadLength_is_negative()
    {
        var message = new PeerMessage
        {
            Payload = null
        };

        Assert.That(message.Length, Is.EqualTo(1));
    }

    [Test]
    public void PayloadLength_should_default_to_negative_one()
    {
        var message = new PeerMessage();

        Assert.That(message.PayloadLength, Is.EqualTo(-1));
    }

    [TestCase(PeerMessageType.Choke)]
    [TestCase(PeerMessageType.Unchoke)]
    [TestCase(PeerMessageType.Interested)]
    [TestCase(PeerMessageType.NotInterested)]
    [TestCase(PeerMessageType.Have)]
    [TestCase(PeerMessageType.Bitfield)]
    [TestCase(PeerMessageType.Request)]
    [TestCase(PeerMessageType.Piece)]
    [TestCase(PeerMessageType.Cancel)]
    [TestCase(PeerMessageType.Port)]
    [TestCase(PeerMessageType.Extended)]
    public void Type_should_store_all_message_types(PeerMessageType type)
    {
        var message = new PeerMessage { Type = type };

        Assert.That(message.Type, Is.EqualTo(type));
    }

    [Test]
    public void Length_should_be_1_when_payload_is_empty_array()
    {
        var message = new PeerMessage
        {
            Type = PeerMessageType.Bitfield,
            Payload = new byte[0]
        };

        Assert.That(message.Length, Is.EqualTo(1));
    }

    [Test]
    public void Length_should_be_1_when_PayloadLength_is_zero()
    {
        var message = new PeerMessage
        {
            Payload = new byte[50],
            PayloadLength = 0
        };

        Assert.That(message.Length, Is.EqualTo(1));
    }

    [Test]
    public void Length_should_use_PayloadLength_over_actual_payload_length()
    {
        var message = new PeerMessage
        {
            Payload = new byte[200],
            PayloadLength = 10
        };

        Assert.That(message.Length, Is.EqualTo(11));
    }

    [Test]
    public void Payload_should_default_to_null()
    {
        var message = new PeerMessage();

        Assert.That(message.Payload, Is.Null);
    }

    [Test]
    public void Type_should_default_to_choke()
    {
        var message = new PeerMessage();

        Assert.That(message.Type, Is.EqualTo(PeerMessageType.Choke));
    }
}
