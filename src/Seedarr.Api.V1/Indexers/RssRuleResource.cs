using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Indexers;

public class RssRuleResource : RestResource
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    [StringLength(1024)]
    public string MustContain { get; set; }

    [StringLength(1024)]
    public string MustNotContain { get; set; }

    [Range(0, int.MaxValue)]
    public int MinSeeders { get; set; } = 1;

    [Range(0, long.MaxValue)]
    public long MinSizeBytes { get; set; }

    [Range(0, long.MaxValue)]
    public long MaxSizeBytes { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxAgeDays { get; set; }

    public bool FreeleechOnly { get; set; }

    [Range(0, int.MaxValue)]
    public int CategoryId { get; set; }

    private List<int> _indexerIds = new();

    [JsonConverter(typeof(IntListOrCommaSeparatedConverter))]
    public List<int> IndexerIds
    {
        get => _indexerIds;
        set => _indexerIds = value ?? new List<int>();
    }
}

public class IntListOrCommaSeparatedConverter : JsonConverter<List<int>>
{
    public override bool HandleNull => true;

    public override List<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new List<int>();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<int>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.Number)
                {
                    list.Add(reader.GetInt32());
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    var str = reader.GetString();
                    if (int.TryParse(str, out var val))
                    {
                        list.Add(val);
                    }
                }
            }

            return list;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return new List<int>();
            }

            var list = new List<int>();
            var parts = str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part, out var val))
                {
                    list.Add(val);
                }
            }

            return list;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            return new List<int> { reader.GetInt32() };
        }

        return new List<int>();
    }

    public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteNumberValue(item);
        }

        writer.WriteEndArray();
    }
}
