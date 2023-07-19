using System;
using System.Text.Json.Serialization;

#nullable enable

namespace Inedo.Docker;

[JsonSerializable(typeof(DockerJsonImage))]
[JsonSerializable(typeof(DockerJsonContainer))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class DockerJsonContext : JsonSerializerContext
{
}

internal sealed class DockerJsonImage
{
    public string? ID { get; set; }
    public string? Repository { get; set; }
    public string? Tag { get; set; }
}

internal sealed class DockerJsonContainer
{
    [JsonConverter(typeof(DockerDateTimeOffsetJsonConverter))]
    public DateTimeOffset CreatedAt { get; set; }
    public string? Image { get; set; }
    public string? Names { get; set; }
    public string? State { get; set; }
    public string? Status { get; set; }
}

