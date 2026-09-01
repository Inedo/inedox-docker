using System.Text.Json.Serialization;

namespace Inedo.Extensions.Docker;

[JsonSerializable(typeof(ComposeLogNode))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DockerComposeClientJsonContext : JsonSerializerContext
{
}


public record ComposeLogNode(string Id, string? Parent_id, string Text, int? Percent);