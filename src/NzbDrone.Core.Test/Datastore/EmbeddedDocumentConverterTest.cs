using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore;

[TestFixture]
public class EmbeddedDocumentConverterTest
{
    private EmbeddedDocumentConverter<List<int>> _intListConverter;
    private EmbeddedDocumentConverter<List<string>> _stringListConverter;

    [SetUp]
    public void SetUp()
    {
        _intListConverter = new EmbeddedDocumentConverter<List<int>>();
        _stringListConverter = new EmbeddedDocumentConverter<List<string>>();
    }

    [Test]
    public void Parse_should_return_empty_list_when_value_is_boxed_int_zero()
    {
        var result = _intListConverter.Parse(0);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_should_return_empty_list_when_value_is_boxed_long_zero()
    {
        var result = _intListConverter.Parse(0L);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_should_return_empty_list_when_value_is_boxed_numeric_non_zero()
    {
        var resultInt = _intListConverter.Parse(42);
        var resultLong = _intListConverter.Parse(42L);

        Assert.That(resultInt, Is.Not.Null);
        Assert.That(resultInt, Is.Empty);
        Assert.That(resultLong, Is.Not.Null);
        Assert.That(resultLong, Is.Empty);
    }

    [Test]
    public void Parse_should_return_empty_list_when_value_is_null()
    {
        var result = _intListConverter.Parse(null);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_should_return_empty_list_when_value_is_dbnull()
    {
        var result = _intListConverter.Parse(DBNull.Value);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t\n")]
    public void Parse_should_return_empty_list_when_value_is_empty_or_whitespace(string input)
    {
        var result = _intListConverter.Parse(input);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [TestCase("0")]
    [TestCase("not-valid-json")]
    [TestCase("{invalid}")]
    [TestCase("true")]
    public void Parse_should_return_empty_list_when_value_is_invalid_non_json_or_mismatched(string invalidJson)
    {
        var result = _intListConverter.Parse(invalidJson);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_should_return_empty_list_when_value_is_json_null()
    {
        var result = _intListConverter.Parse("null");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_should_deserialize_valid_json_arrays()
    {
        var emptyResult = _intListConverter.Parse("[]");
        Assert.That(emptyResult, Is.Not.Null);
        Assert.That(emptyResult, Is.Empty);

        var populatedResult = _intListConverter.Parse("[1, 2, 3]");
        Assert.That(populatedResult, Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Parse_should_return_same_instance_when_already_target_type()
    {
        var existing = new List<int> { 10, 20 };
        var result = _intListConverter.Parse(existing);

        Assert.That(result, Is.SameAs(existing));
    }

    [Test]
    public void Parse_should_handle_string_list_converter_safely()
    {
        var numResult = _stringListConverter.Parse(0L);
        Assert.That(numResult, Is.Not.Null);
        Assert.That(numResult, Is.Empty);

        var invalidResult = _stringListConverter.Parse("invalid");
        Assert.That(invalidResult, Is.Not.Null);
        Assert.That(invalidResult, Is.Empty);

        var validResult = _stringListConverter.Parse("[\"tag1\", \"tag2\"]");
        Assert.That(validResult, Is.EquivalentTo(new[] { "tag1", "tag2" }));
    }

    [Test]
    public void SetValue_should_serialize_list_or_fallback_on_null()
    {
        var param = new SqliteParameter();
        _intListConverter.SetValue(param, new List<int> { 1, 2 });
        Assert.That(param.Value, Is.EqualTo("[1,2]"));

        var nullParam = new SqliteParameter();
        _intListConverter.SetValue(nullParam, null);
        Assert.That(nullParam.Value, Is.EqualTo("[]"));
    }
}
