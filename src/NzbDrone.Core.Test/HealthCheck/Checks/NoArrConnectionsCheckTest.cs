using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class NoArrConnectionsCheckTest
{
    private IArrConnectionFactory _connectionFactory;
    private NoArrConnectionsCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _connectionFactory = Substitute.For<IArrConnectionFactory>();
        _subject = new NoArrConnectionsCheck(_connectionFactory);
    }

    [Test]
    public void Check_should_return_notice_when_no_connections()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoArrConnections"));
    }

    [Test]
    public void Check_should_return_notice_when_connections_exist_but_none_enabled()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new ArrConnectionDefinition { Enable = false }
        });

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("NoArrConnections"));
    }

    [Test]
    public void Check_should_return_ok_when_enabled_connection_exists()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>
        {
            new ArrConnectionDefinition { Enable = true }
        });

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(result.Source, Is.EqualTo("NoArrConnections"));
    }
}
