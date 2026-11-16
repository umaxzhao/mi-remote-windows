using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiRemote.Core
{
    public static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new ActionJsonConverter() },
        };
    }

    public static class ConfigJson
    {
        public static string Serialize(MappingConfig config) => JsonSerializer.Serialize(config, JsonOptions.Default);

        /// <summary>严格反序列化：任何未知动作 type / 坏字段都会抛异常（调用方回退默认配置）。</summary>
        public static MappingConfig? Deserialize(string json) => JsonSerializer.Deserialize<MappingConfig>(json, JsonOptions.Default);

        public static MAction? DeserializeAction(string json) => JsonSerializer.Deserialize<MAction>(json, JsonOptions.Default);
    }
}
