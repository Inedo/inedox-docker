using System.Text.Json.Serialization;

namespace Inedo.Extensions.Docker;

[JsonSerializable(typeof(RootLogNode))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class DockerClientJsonContext : JsonSerializerContext
{
}

internal sealed record class Vertex(string Digest, string Name, DateTime? Started, DateTime? Completed);

internal sealed record class Status(string Id, string Vertex, string Name, long Current, DateTime Timestamp, DateTime Started, DateTime? Completed);

internal sealed record class LogNode(string Vertex, int Stream, byte[] Data, DateTime Timestamp);

internal sealed record class RootLogNode(Vertex[]? Vertexes, Status[]? Statuses, LogNode[]? Logs);
