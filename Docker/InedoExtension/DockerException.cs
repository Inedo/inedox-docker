namespace Inedo.Extensions.Docker;

internal sealed class DockerException(int exitCode, string? message = null) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
