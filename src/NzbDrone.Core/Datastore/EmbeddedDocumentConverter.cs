using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using Dapper;

namespace NzbDrone.Core.Datastore;

public class EmbeddedDocumentConverter<T> : SqlMapper.TypeHandler<T>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.Value = JsonSerializer.Serialize(value ?? CreateFallback(), Options);
    }

    public override T Parse(object value)
    {
        if (value is T typedValue)
        {
            return typedValue;
        }

        if (value == null || value is DBNull)
        {
            return CreateFallback();
        }

        if (value is not string json || string.IsNullOrWhiteSpace(json))
        {
            return CreateFallback();
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(json, Options);
            return result ?? CreateFallback();
        }
        catch (JsonException)
        {
            return CreateFallback();
        }
        catch (Exception)
        {
            return CreateFallback();
        }
    }

    private static T CreateFallback()
    {
        if (typeof(T) == typeof(List<int>))
        {
            return (T)(object)new List<int>();
        }

        if (typeof(T) == typeof(List<string>))
        {
            return (T)(object)new List<string>();
        }

        try
        {
            return Activator.CreateInstance<T>() ?? default!;
        }
        catch
        {
            return default!;
        }
    }
}
