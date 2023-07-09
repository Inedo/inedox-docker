#nullable enable

using System;

namespace Inedo.Docker;

public sealed record class ContainerInfo(string Name, string Image, string? Digest, DateTimeOffset CreatedDate, string State, string? Status);
