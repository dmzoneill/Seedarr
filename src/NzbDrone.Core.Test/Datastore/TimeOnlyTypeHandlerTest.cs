using System;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class TimeOnlyTypeHandlerTest
{
    private TimeOnlyTypeHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _handler = new TimeOnlyTypeHandler();
    }

    [Test]
    public void Parse_should_return_same_instance_when_already_time_only()
    {
        var existing = new TimeOnly(9, 30, 15);
        var result = _handler.Parse(existing);

        Assert.That(result, Is.EqualTo(existing));
    }

    [Test]
    public void Parse_should_return_midnight_when_value_is_null()
    {
        var result = _handler.Parse(null);

        Assert.That(result, Is.EqualTo(default(TimeOnly)));
    }

    [Test]
    public void Parse_should_return_midnight_when_value_is_dbnull()
    {
        var result = _handler.Parse(DBNull.Value);

        Assert.That(result, Is.EqualTo(default(TimeOnly)));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n")]
    public void Parse_should_return_midnight_when_value_is_empty_or_whitespace(string input)
    {
        var result = _handler.Parse(input);

        Assert.That(result, Is.EqualTo(default(TimeOnly)));
    }

    [TestCase("not-a-time")]
    [TestCase("25:00:00")]
    [TestCase("12:99:99")]
    public void Parse_should_return_midnight_when_value_is_malformed(string invalid)
    {
        var result = _handler.Parse(invalid);

        Assert.That(result, Is.EqualTo(default(TimeOnly)));
    }

    [Test]
    public void Parse_should_return_midnight_when_value_is_non_string()
    {
        var result = _handler.Parse(42);

        Assert.That(result, Is.EqualTo(default(TimeOnly)));
    }

    [TestCase("08:30:00", 8, 30, 0)]
    [TestCase("23:59:59", 23, 59, 59)]
    public void Parse_should_deserialize_valid_time_strings(string input, int hour, int minute, int second)
    {
        var result = _handler.Parse(input);

        Assert.That(result, Is.EqualTo(new TimeOnly(hour, minute, second)));
    }

    [Test]
    public void SetValue_should_serialize_as_hh_mm_ss()
    {
        var param = new SqliteParameter();
        _handler.SetValue(param, new TimeOnly(14, 5, 9));

        Assert.That(param.Value, Is.EqualTo("14:05:09"));
    }
}
