using System.Text.Json.Serialization;
using carton.GUI.Services;

namespace carton.GUI.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(ClashConfigSnapshot))]
[JsonSerializable(typeof(MainWindowState))]
internal partial class CartonGuiJsonContext : JsonSerializerContext;
