using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Docker;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.Operations;
using Inedo.Extensions.SecureResources;

#nullable enable

namespace Inedo.Extensions.Docker;

internal sealed class DockerClientEx : DockerClient
{
    private readonly IOperationExecutionContext context;
    private readonly Func<string, string> escapeArg;

    public string EscapeArg(string arg) => this.escapeArg(arg);

    private DockerClientEx(IOperationExecutionContext context, Func<string, string> escapeArg, DockerClientType? type, string? dockerExePath)
        : base(context.Agent, type, dockerExePath)
    {
        this.context = context;
        this.escapeArg = escapeArg;
    }

    public static async Task<DockerClientEx> CreateAsync(DockerOperation operation, IOperationExecutionContext context)
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

        return new DockerClientEx(context, (await context.Agent.GetServiceAsync<IRemoteProcessExecuter>()).EscapeArg, type, dockerExePath);
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

    public async Task<int> Docker2Async(string args, Action<string>? outputReceived = null, Action<string>? errorReceived = null)
    {
        var logScopes = new Dictionary<int, IScopedLog>();

        this.context.Log.LogDebug($"Executing docker {args}");
        int exitCode = await this.DockerAsync(
            args,
            outputReceived ?? this.context.Log.LogInformation,
            errorReceived ?? this.context.Log.LogError,
            this.context.CancellationToken
        );

        if (exitCode != 0)
            throw new ExecutionFailureException($"Unexpected exit code: {exitCode}");

        this.context.Log.LogDebug($"Docker exited with code: {exitCode}");

        return exitCode;
    }

    public async Task<int> DockerAsync(string args, bool processBuildErrors = false, bool failOnErrors = true)
    {
        var logScopes = new Dictionary<int, IScopedLog>();
        IScopedLog? lastLog = null;
        var lastLogLevel = MessageLevel.Error;

        this.context.Log.LogDebug($"Executing docker {args}");
        var exitCode = await this.DockerAsync(
            args,
            this.context.Log.LogInformation,
            failOnErrors
                ? (processBuildErrors ? LogBuildError : this.context.Log.LogError)
                : this.context.Log.LogWarning,
            this.context.CancellationToken);

        if (failOnErrors && exitCode != 0)
            throw new ExecutionFailureException($"Unexpected exit code: {exitCode}");
        this.context.Log.LogDebug($"Docker exited with code: {exitCode}");

        return exitCode;

        void LogBuildError(string text)
        {
            if (text.StartsWith('#') && text.Contains(' ') && int.TryParse(text.AsSpan(1, text.IndexOf(' ') - 1), out var scopeNum))
            {
                var message = text[(text.IndexOf(' ') + 1)..];
                var firstWord = message[..Math.Max(message.IndexOf(' '), 0)];

                bool finished = false;
                MessageLevel level;
                if (decimal.TryParse(firstWord, out _))
                {
                    level = MessageLevel.Debug;
                    message = message[(message.IndexOf(' ') + 1)..].TrimEnd('\r');
                    message = message[(message.LastIndexOf('\r') + 1)..];
                }
                else if (firstWord == "DONE")
                {
                    level = MessageLevel.Information;
                    finished = true;
                }
                else if (firstWord == "ERROR")
                {
                    level = MessageLevel.Error;
                    finished = true;
                }
                else
                {
                    level = MessageLevel.Information;
                }

                if (logScopes.TryGetValue(scopeNum, out var logScope))
                {
                    logScope.Log(level, message);
                    lastLog = logScope;
                }
                else
                {
                    logScope = context.Log.CreateNestedLog($"{scopeNum}. {message}");
                    logScopes[scopeNum] = logScope;
                    lastLog = logScope;
                }

                if (finished)
                {
                    lastLog.Dispose();
                    lastLog = null;
                }

                lastLogLevel = level;
            }
            else
            {
                // a continuation of the previous non-build-process message
                this.context.Log.Log(lastLogLevel, text.TrimEnd('\r'));
            }
        }
    }

    public Task<string> GetDigestAsync(string repositoryAndTag) => this.GetDigestAsync(repositoryAndTag, this.context.CancellationToken);
}
