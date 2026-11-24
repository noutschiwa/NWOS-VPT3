using System.Text.Json.Serialization;

namespace NWOS.Cardinal;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AliceSearchResponse))]
internal partial class CardinalJsonContext : JsonSerializerContext
{
}
