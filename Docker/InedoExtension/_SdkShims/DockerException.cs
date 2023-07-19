#nullable enable

using System;

namespace Inedo.Docker;

public class DockerException : Exception
{
    public DockerException(int exitCode, string? message = null) : base(message)
    {
        this.ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
