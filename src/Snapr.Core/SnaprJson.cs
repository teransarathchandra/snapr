using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snapr.Core;

public static class SnaprJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
