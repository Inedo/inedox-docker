using System.Text;
using Inedo.Agents;
using Inedo.Extensibility.Agents;

#nullable enable

namespace Inedo.Docker;

public class DockerClient
{
    private readonly IRemoteProcessExecuter remoteProcessExecuter;
    private readonly DockerClientType? type;
    private readonly string? dockerExecPath;
    private string? loggedInRegistry;

    protected DockerClient(Agent agent, DockerClientType? type, string? dockerExecPath)
    {
        ArgumentNullException.ThrowIfNull(agent);
        this.remoteProcessExecuter = agent.GetService<IRemoteProcessExecuter>();
        this.type = type;
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
    protected static async Task<(DockerClientType type, int majorVersion)?> DetectClientTypeAsync(Agent agent, CancellationToken cancellationToken)
    {
        var proccessExec = await agent.GetServiceAsync<IRemoteProcessExecuter>().ConfigureAwait(false);

        bool isLinux = (await agent.GetServiceAsync<IFileOperationsExecuter>()).DirectorySeparator == '/';
        if (isLinux)
        {
            var res = await CheckForDockerAsync(proccessExec, DockerClientType.Linux, cancellationToken);
            if (res.HasValue)
                return (DockerClientType.Linux, res.GetValueOrDefault());

            return null;
        }


        var res2 = await CheckForDockerAsync(proccessExec, DockerClientType.Windows, cancellationToken);
        if (res2.HasValue)
            return (DockerClientType.Windows, res2.GetValueOrDefault());

        res2 = await CheckForDockerAsync(proccessExec, DockerClientType.Wsl, cancellationToken);
        if (res2.HasValue)
            return (DockerClientType.Wsl, res2.GetValueOrDefault());

        return null;
    }

    /// <summary>
    /// Checks for Docker on a server and gets version information if it is installed.
    /// </summary>
    /// <param name="exec">Remote process executer for the server.</param>
    /// <param name="type">Type of client to verify</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Docker version string when Docker is installed on the server; otherwise null.</returns>
    public static async Task<int?> CheckForDockerAsync(IRemoteProcessExecuter exec, DockerClientType type, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exec);

        try
        {
            const string VersionArgs = "version --format '{{.Client.Version}}'";

            await using var process = exec.CreateProcess(
                new RemoteProcessStartInfo
                {
                    FileName = type switch
                    {
                        DockerClientType.Linux => "docker",
                        DockerClientType.Windows => "docker.exe",
                        DockerClientType.Wsl => "wsl.exe",
                        _ => throw new ArgumentOutOfRangeException(nameof(type))
                    },
                    Arguments = type == DockerClientType.Wsl ? $"docker {VersionArgs}" : VersionArgs,
                }
            );

            var readLock = new Lock();

            var sb = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                lock (readLock)
                    sb.Append(e.Data);
            };

            await process.StartAsync(cancellationToken);
            await process.WaitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                var fullVersion = sb.ToString().Trim().Trim('\'');
                int dotIndex = fullVersion.IndexOf('.');
                if (dotIndex > 0 && int.TryParse(fullVersion.AsSpan(0, dotIndex), out int majorVersion))
                    return majorVersion;
            }
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

    public async Task DockerLogoutAsync(string registry, CancellationToken cancellationToken = default)
    {
        this.loggedInRegistry = registry;
        await DockerLogoutAsync(cancellationToken);
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
