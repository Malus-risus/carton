using System.Text.Json.Serialization;
using carton.GUI.Services;

namespace carton.GUI.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(MainWindowState))]
[JsonSerializable(typeof(SingBoxDashboardServersState))]
[JsonSerializable(typeof(SingBoxDashboardServer))]
internal partial class CartonGuiJsonContext : JsonSerializerContext;
