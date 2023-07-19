using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Extensibility.Agents;
using Inedo.Extensibility.Credentials;
using Inedo.Extensions.SecureResources;
using Inedo.Web.Controls.Layout;
#nullable enable

namespace Inedo.Docker;

public sealed class DockerClient
{
    private readonly IRemoteProcessExecuter remoteProcessExecuter;
    private readonly DockerClientType? type;
    private readonly string? dockerExecPath;
    private string? loggedInRegistry;

    /// <summary>
    /// Creates a new instance with a known client type
    /// </summary>
    public DockerClient(Agent agent, DockerClientType type)
    {
        ArgumentNullException.ThrowIfNull(agent);
        this.remoteProcessExecuter = agent.GetService<IRemoteProcessExecuter>();
        this.type = type;
    }
    /// <summary>
    /// Creates a new instance with a custom executable name
    /// </summary>
    public DockerClient(Agent agent, string dockerExecPath)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (string.IsNullOrEmpty(dockerExecPath))
            throw new ArgumentNullException(nameof(dockerExecPath));

        this.remoteProcessExecuter = agent.GetService<IRemoteProcessExecuter>();
        this.dockerExecPath = dockerExecPath;
    }

    public event EventHandler<ProcessDataReceivedEventArgs>? OutputReceived;
    public event EventHandler<ProcessDataReceivedEventArgs>? ErrorReceived;
    public DockerClientType? ClientType => this.type;
    private RemoteProcessStartInfo NewDockerStartInfo(string args, bool useUTF8ForStandardOutput = false, bool redirectStandardInput = false) => new()
    {
        FileName = this.type switch
        {
            null => this.dockerExecPath,
            DockerClientType.Linux => "docker",
            DockerClientType.Windows => "docker.exe",
            DockerClientType.Wsl => "wsl.exe",
            _ => throw new InvalidOperationException($"Unexpected DockerClientType:{this.type}")
        },
        Arguments = type == DockerClientType.Wsl ? $"docker {args}" : args,
        UseUTF8ForStandardOutput = useUTF8ForStandardOutput,
        RedirectStandardInput = redirectStandardInput
    };

    /// <summary>
    /// Looks for an installed Docker client type using "docker -v"
    /// </summary>
    /// <remarks>
    /// When Docker for Windows and Docker (WSL) are both present on a Windows system, Docker for Windows is returned
    /// </remarks>
    public static async Task<DockerClientType?> DetectClientTypeAsync(Agent agent, CancellationToken cancellationToken)
    {
        var proccessExec = await agent.GetServiceAsync<IRemoteProcessExecuter>().ConfigureAwait(false);

        if (await agent.TryGetServiceAsync<ILinuxFileOperationsExecuter>().ConfigureAwait(false) is not null)
        {
            if (await CheckForDockerAsync(proccessExec, DockerClientType.Linux, cancellationToken).ConfigureAwait(false) is not null)
                return DockerClientType.Linux;

            return null;
        }

        if (await CheckForDockerAsync(proccessExec, DockerClientType.Windows, cancellationToken).ConfigureAwait(false) is not null)
            return DockerClientType.Windows;

        if (await CheckForDockerAsync(proccessExec, DockerClientType.Wsl, cancellationToken).ConfigureAwait(false) is not null)
            return DockerClientType.Wsl;

        return null;
    }

    /// <summary>
    /// Checks for Docker on a server and gets version information if it is installed.
    /// </summary>
    /// <param name="exec">Remote process executer for the server.</param>
    /// <param name="type">Type of client to verify</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Docker version string when Docker is installed on the server; otherwise null.</returns>
    public static async Task<string?> CheckForDockerAsync(IRemoteProcessExecuter exec, DockerClientType type, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exec);

        try
        {
            await using var process = exec.CreateProcess(
                new()
                {
                    FileName = type switch
                    {
                        DockerClientType.Linux => "docker",
                        DockerClientType.Windows => "docker.exe",
                        DockerClientType.Wsl => "wsl.exe",
                        _ => throw new ArgumentOutOfRangeException(nameof(type))
                    },
                    Arguments = type == DockerClientType.Wsl ? $"docker -v" : "-v",
                }
            );

            var sb = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                lock (sb)
                    sb.Append(e.Data);
            };

            await process.StartAsync(cancellationToken);
            await process.WaitAsync(cancellationToken);

            if (process.ExitCode == 0)
                return AH.NullIf(sb.ToString().Trim(), string.Empty);
        }
        catch
        {
        }

        return null;
    }
    public async Task<string> GetDigestAsync(string repositoryAndTag, CancellationToken cancellationToken = default)
    {
        var lines = await this.ReadDockerLinesAsync("inspect --format='{{.Id}}' " + this.remoteProcessExecuter.EscapeArg(repositoryAndTag), cancellationToken);
        if (lines.Count != 1)
            throw new DockerException(0, $"inspect returned unexpected output ({lines.Count} lines instead of 1): {string.Join("\\n", lines)}");
        return lines[0].Trim();
    }
    public async Task<IReadOnlyList<ContainerInfo>> GetContainersAsync(CancellationToken cancellationToken = default)
    {
        var imageLookup = new Dictionary<string, string>();

        foreach (var line in await this.ReadDockerLinesAsync("images --format json --no-trunc", cancellationToken))
        {
            var image = JsonSerializer.Deserialize(line, DockerJsonContext.Default.DockerJsonImage);
            if (image == null || string.IsNullOrEmpty(image.ID) || string.IsNullOrEmpty(image.Repository) || string.IsNullOrEmpty(image.Tag))
                continue;

            imageLookup[$"{image.Repository}:{image.Tag}"] = image.ID;
        }

        var containers = new List<ContainerInfo>();

        foreach (var line in await this.ReadDockerLinesAsync("ps -a --format json --no-trunc", cancellationToken))
        {
            var container = JsonSerializer.Deserialize(line, DockerJsonContext.Default.DockerJsonContainer);
            if (container == null || string.IsNullOrEmpty(container.Image) || string.IsNullOrEmpty(container.Names) || string.IsNullOrEmpty(container.State))
                continue;

            if (imageLookup.TryGetValue(container.Image, out var digest))
                containers.Add(new ContainerInfo(container.Names, container.Image, digest, container.CreatedAt, container.State, container.Status));
            else if (container.State == "running")
                containers.Add(new ContainerInfo(container.Names, container.Image, null, container.CreatedAt, container.State, container.Status));
        }

        return containers;
    }

    public async Task<int> DockerAsync(string arguments, Action<string>? outputReceived = null, Action<string>? errorReceived = null, CancellationToken cancellationToken = default)
    {
        await using var process = this.remoteProcessExecuter.CreateProcess(
            this.NewDockerStartInfo(arguments)
        );

        if (outputReceived != null)
            process.OutputDataReceived += (s, e) => outputReceived(e.Data);
        else
            process.OutputDataReceived += (s, e) => this.OutputReceived?.Invoke(this, e);

        if (errorReceived != null)
            process.ErrorDataReceived += (s, e) => errorReceived(e.Data);
        else
            process.ErrorDataReceived += (s, e) => this.ErrorReceived?.Invoke(this, e);

        await process.StartAsync(cancellationToken);
        await process.WaitAsync(cancellationToken).ConfigureAwait(false);

        return process.ExitCode.GetValueOrDefault();
    }


    public async Task DockerLoginAsync(string registry, string userName, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(registry))
            throw new ArgumentNullException(nameof(registry));
        if (string.IsNullOrEmpty(userName))
            throw new ArgumentNullException(nameof(userName));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));

        await using var process = this.remoteProcessExecuter.CreateProcess(
            this.NewDockerStartInfo(
                $"login \"{registry}\" --username \"{userName}\" --password-stdin",
                redirectStandardInput: true
            )
        );

        process.OutputDataReceived += (s, e) => this.OutputReceived?.Invoke(this, e);
        process.ErrorDataReceived += (s, e) => this.ErrorReceived?.Invoke(this, e);

        await process.StartAsync(cancellationToken);
        process.StandardInput.Write(password);
        await process.StandardInput.DisposeAsync();

        await process.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new DockerException(process.ExitCode.GetValueOrDefault());

        this.loggedInRegistry = registry;
    }

    public async Task DockerLogoutAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(this.loggedInRegistry))
            return;

        await using var process = this.remoteProcessExecuter.CreateProcess(
            this.NewDockerStartInfo($"logout \"{this.loggedInRegistry}\"")
        );

        process.OutputDataReceived += (s, e) => this.OutputReceived?.Invoke(this, e);
        process.ErrorDataReceived += (s, e) => this.ErrorReceived?.Invoke(this, e);

        await process.StartAsync(cancellationToken);
        await process.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new DockerException(process.ExitCode.GetValueOrDefault());
    }

    private static void ReadOutput(IRemoteProcess process, out List<string> output, out List<string> error)
    {
        var opt = new List<string>();
        var err = new List<string>();

        process.OutputDataReceived += (s, e) => { lock (opt) opt.Add(e.Data); };
        process.ErrorDataReceived += (s, e) => { lock (err) err.Add(e.Data); };

        output = opt;
        error = err;
    }

    private async Task<List<string>> ReadDockerLinesAsync(string args, CancellationToken cancellationToken)
    {
        await using var process = this.remoteProcessExecuter.CreateProcess(
            this.NewDockerStartInfo(
                args,
                useUTF8ForStandardOutput: true
            )
        );

        ReadOutput(process, out var output, out var error);

        await process.StartAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new DockerException(process.ExitCode.GetValueOrDefault(), string.Join(' ', error));

        return output;
    }
}
