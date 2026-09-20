using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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

    public bool AllowUnknownSeeders { get; set; } = true;

    [Range(0, int.MaxValue)]
    public int Priority { get; set; }

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

    private List<int> _tags = new();

    [JsonConverter(typeof(IntListOrCommaSeparatedConverter))]
    public List<int> Tags
    {
        get => _tags;
        set => _tags = value ?? new List<int>();
    }

    [JsonConverter(typeof(IntListOrCommaSeparatedConverter))]
    public List<int> TagIds
    {
        get => Tags;
        set => Tags = value ?? new List<int>();
    }

    private List<string> _allowedResolutions = new();

    [JsonConverter(typeof(StringListOrCommaSeparatedConverter))]
    public List<string> AllowedResolutions
    {
        get => _allowedResolutions;
        set => _allowedResolutions = value ?? new List<string>();
    }

    private List<string> _allowedSources = new();

    [JsonConverter(typeof(StringListOrCommaSeparatedConverter))]
    public List<string> AllowedSources
    {
        get => _allowedSources;
        set => _allowedSources = value ?? new List<string>();
    }

    private List<string> _allowedCodecs = new();

    [JsonConverter(typeof(StringListOrCommaSeparatedConverter))]
    public List<string> AllowedCodecs
    {
        get => _allowedCodecs;
        set => _allowedCodecs = value ?? new List<string>();
    }

    public string SavePath { get; set; }

    public bool SequentialDownload { get; set; }

    public string InitialStatus { get; set; }
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

public class StringListOrCommaSeparatedConverter : JsonConverter<List<string>>
{
    public override bool HandleNull => true;

    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new List<string>();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    var val = reader.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
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
                return new List<string>();
            }

            return str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        return new List<string>();
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
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
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}
