using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SubscriptionLibraries.Core.Utilities;

internal static class SafeJson
{
    private const int MaximumDepth = 64;

    public static JToken ParseToken(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        using var stringReader = new StringReader(json);
        using var jsonReader = CreateReader(stringReader);
        var token = JToken.Load(jsonReader);
        EnsureNoTrailingContent(jsonReader);
        return token;
    }

    public static T? Deserialize<T>(string json, JsonSerializerSettings? settings = null)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        using var stringReader = new StringReader(json);
        using var jsonReader = CreateReader(stringReader);
        var serializer = settings is null
            ? JsonSerializer.CreateDefault()
            : JsonSerializer.Create(settings);
        var value = serializer.Deserialize<T>(jsonReader);
        EnsureNoTrailingContent(jsonReader);
        return value;
    }

    private static JsonTextReader CreateReader(TextReader reader) => new(reader)
    {
        DateParseHandling = DateParseHandling.None,
        MaxDepth = MaximumDepth
    };

    private static void EnsureNoTrailingContent(JsonReader reader)
    {
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.Comment)
            {
                throw new JsonReaderException("Additional JSON content followed the root value.");
            }
        }
    }
}
