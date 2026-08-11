using System.Text;
using Inedo.Agents;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Agents;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.Operations;
using Inedo.Extensions.SecureResources;

namespace Inedo.Extensions.Docker;

internal sealed class DockerClient
{
    private readonly IRemoteProcessExecuter remoteProcessExecuter;
    private readonly string? dockerExecPath;
    private string? loggedInRegistry;
    private readonly IOperationExecutionContext context;
    private readonly Func<string, string> escapeArg;

    private DockerClient(IOperationExecutionContext context, Func<string, string> escapeArg, DockerClientType? type, string? dockerExePath)
    {
        this.remoteProcessExecuter = context.Agent.GetService<IRemoteProcessExecuter>();
        this.ClientType = type;
        this.dockerExecPath = dockerExePath;
        this.context = context;
        this.escapeArg = escapeArg;
    }

    public DockerClientType? ClientType { get; }

    public string EscapeArg(string arg) => this.escapeArg(arg);

    public static async Task<DockerClient> CreateAsync(DockerOperation operation, IOperationExecutionContext context)
    {
        DockerClientType? type = null;
        string? dockerExePath = null;
        int version = 0;

        if (string.IsNullOrEmpty(operation.DockerExePath) || operation.DockerExePath == "docker")
        {
            var res = await DetectClientTypeAsync(context.Agent, context.CancellationToken)
                ?? throw new ExecutionFailureException("A Docker client was not detected on this server.");

            type = res.type;
            version = res.majorVersion;
        }
        else
        {
            if (operation.UseWsl)
                operation.LogWarning($"{nameof(operation.UseWsl)} is ignored when {nameof(operation.DockerExePath)} is specified.");

            operation.LogWarning($"{nameof(operation.DockerExePath)} is deprecated and support for custom docker paths will be removed in a future version.");

            dockerExePath = operation.DockerExePath;
        }

        return new DockerClient(context, (await context.Agent.GetServiceAsync<IRemoteProcessExecuter>()).EscapeArg, type, dockerExePath);
    }

    public async Task LoginAsync(DockerRepository repoResource)
    {
        if (repoResource == null)
            return;

        var userpass = repoResource.GetDockerCredentials(context);
        if (userpass == null)
        {
            context.Log.LogDebug("No credentials are specified for Docker repository; skipping docker login.");
            return;
        }

        var repository = repoResource.GetRepository(context)
            ?? throw new ExecutionFailureException("Docker repository did not specify a usable repository name.");

        var repositoryParts = repository.Split('/');
        if (repositoryParts.Length < 2)
            throw new ExecutionFailureException($"Docker repository specified an invalid repository format: \"{repository}\"");

        try
        {
            await this.DockerLoginAsync(repositoryParts[0], userpass.UserName, AH.Unprotect(userpass.Password), context.CancellationToken);
        }
        catch (DockerException ex)
        {
            // login invalid
            // not in sudoers
            // insecure registry
            // cannot reach host
            context.Log.LogInformation(
                $"Failed to log in to Docker registry \"{repositoryParts[0]}\" (exit code {ex.ExitCode}). " +
                "Common causes include: " +
                "the Docker CLI is not permitted to run (check sudoers or the docker group on Linux), " +
                "the registry uses HTTP or a self-signed certificate and is not listed as an insecure registry in the Docker daemon configuration, " +
                "or the credentials are invalid / the registry is unreachable. " +
                "Verify that the Docker CLI can reach the registry and authenticate successfully."
            );

            context.Log.LogError($"Failed to log in to Docker registry \"{repositoryParts[0]}\" with exit code {ex.ExitCode}");
        }
    }

    public async Task LogoutAsync(DockerRepository repoResource)
    {
        if (repoResource == null)
            return;

        var repository = repoResource.GetRepository(context)
            ?? throw new ExecutionFailureException($"Docker repository did not specify a usable repository name.");

        var repositoryParts = repository.Split('/');
        if (repositoryParts.Length < 2)
            throw new ExecutionFailureException($"Docker repository specified an invalid repository format: \"{repository}\"");

        try
        {
            await this.DockerLogoutAsync(repositoryParts[0], context.CancellationToken);
        }
        catch (DockerException ex)
        {
            context.Log.LogError($"Failed to logout of Docker registry \"{repositoryParts[0]}\" with exit code {ex.ExitCode}");
        }
    }

    public async Task<int> DockerAsync(string args, Action<string>? outputReceived = null, Action<string>? errorReceived = null, bool failOnErrors = true)
    {
        await using var process = this.remoteProcessExecuter.CreateProcess(this.NewDockerStartInfo(args));
        
        EventHandler<ProcessDataReceivedEventArgs> handleOutput;
        if (outputReceived is not null)
            handleOutput = (_, e) => outputReceived(e.Data);
        else
            handleOutput = (_, e) => this.context.Log.LogInformation(e.Data);
        
        EventHandler<ProcessDataReceivedEventArgs> handleError;
        if (errorReceived is not null)
            handleError = (_, e) => errorReceived(e.Data);
        else
            handleError = (_, e) => this.context.Log.LogError(e.Data);

        process.OutputDataReceived += handleOutput;
        process.ErrorDataReceived += handleError;

        this.context.Log.LogDebug($"Executing docker {args}");

        await process.StartAsync(this.context.CancellationToken);
        await process.WaitAsync(this.context.CancellationToken);

        int exitCode = process.ExitCode.GetValueOrDefault();
        if (failOnErrors && exitCode != 0)
            throw new ExecutionFailureException($"Unexpected exit code: {exitCode}");

        this.context.Log.LogDebug($"Docker exited with code: {exitCode}");
        return exitCode;
    }

    public Task<string> GetDigestAsync(string repositoryAndTag) => this.GetDigestAsync(repositoryAndTag, this.context.CancellationToken);

    public async Task<string> GetDigestAsync(string repositoryAndTag, CancellationToken cancellationToken = default)
    {
        var lines = await this.ReadDockerLinesAsync("inspect --format='{{.Id}}' " + this.remoteProcessExecuter.EscapeArg(repositoryAndTag), cancellationToken);
        if (lines.Count != 1)
            throw new DockerException(0, $"inspect returned unexpected output ({lines.Count} lines instead of 1): {string.Join("\\n", lines)}");
        return lines[0].Trim();
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

        await process.StartAsync(cancellationToken);
        await process.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new DockerException(process.ExitCode.GetValueOrDefault());
    }

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

    private static async Task<(DockerClientType type, int majorVersion)?> DetectClientTypeAsync(Agent agent, CancellationToken cancellationToken)
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

    private RemoteProcessStartInfo NewDockerStartInfo(string args, bool useUTF8ForStandardOutput = false, bool redirectStandardInput = false) => new()
    {
        FileName = this.ClientType switch
        {
            null => this.dockerExecPath,
            DockerClientType.Linux => "docker",
            DockerClientType.Windows => "docker.exe",
            DockerClientType.Wsl => "wsl.exe",
            _ => throw new InvalidOperationException($"Unexpected DockerClientType:{this.ClientType}")
        },
        Arguments = ClientType == DockerClientType.Wsl ? $"docker {args}" : args,
        UseUTF8ForStandardOutput = useUTF8ForStandardOutput,
        RedirectStandardInput = redirectStandardInput
    };
}
