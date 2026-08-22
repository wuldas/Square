using System.Text.Json.Serialization;

namespace Square.Hosting.Web;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SquareWebEventRequest))]
[JsonSerializable(typeof(SquareWebEventResponse))]
internal sealed partial class SquareWebJsonContext : JsonSerializerContext;

internal sealed record SquareWebEventResponse(
    string BodyHtml,
    string Css,
    long Revision,
    bool DefaultPrevented);
