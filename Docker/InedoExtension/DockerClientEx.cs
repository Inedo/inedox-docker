using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Docker;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.Operations;
using Inedo.Extensions.SecureResources;

#nullable enable

namespace Inedo.Extensions.Docker;
internal sealed class DockerClientEx
{
    private readonly DockerClient client;
    private readonly IOperationExecutionContext context;
    private readonly Func<string, string> escapeArg;

    public DockerClientType? ClientType => this.client.ClientType;
    public string EscapeArg(string arg) => this.escapeArg(arg);

    private DockerClientEx(DockerClient client, IOperationExecutionContext context, Func<string, string> escapeArg)
    {
        this.client = client;
        this.context = context;
        this.escapeArg = escapeArg;
    }
    public static async Task<DockerClientEx> CreateAsync(DockerOperation_ForTheNew operation, IOperationExecutionContext context)
    {
        DockerClient client;
        if (string.IsNullOrEmpty(operation.DockerExePath) || operation.DockerExePath == "docker")
        {
            var type = await DockerClient.DetectClientTypeAsync(context.Agent, context.CancellationToken);
            if (type == null)
                throw new ExecutionFailureException("A Docker client was not detected on this server.");
            client = new DockerClient(context.Agent, type.Value);
        }
        else
        {
            if (operation.UseWsl)
                operation.LogWarning($"{nameof(operation.UseWsl)} is ignored when {nameof(operation.DockerExePath)} is specified.");
            operation.LogWarning($"{nameof(operation.DockerExePath)} is deprecated and support for custom docker paths will be removed in a future version");
            client = new DockerClient(context.Agent, operation.DockerExePath);
        }

        return new(client, context, (await context.Agent.GetServiceAsync<IRemoteProcessExecuter>()).EscapeArg);
    }
    

    public async Task LoginAsync(DockerRepository repoResource)
    {
        if (repoResource == null)
            return;

        var userpass = repoResource.GetDockerCredentials(context);
        if (userpass == null)
        {
            context.Log.LogDebug($"No credentials are specified for Docker repository; skipping docker login.");
            return;
        }

        var repository = repoResource.GetRepository(context)
            ?? throw new ExecutionFailureException($"Docker repository did not specify a usable repository name.");

        var repositoryParts = repository.Split('/');
        if (repositoryParts.Length < 2)
            throw new ExecutionFailureException($"Docker repository specified an invalid repository format: \"{repository}\"");

        await this.client.DockerLoginAsync(repositoryParts[0], userpass.UserName, AH.Unprotect(userpass.Password), context.CancellationToken);
    }
    public Task LogoutAsync() => client.DockerLogoutAsync(this.context.CancellationToken);

    public async Task<int> DockerAsync(string args, bool processBuildErrors = false, bool failOnErrors = true)
    {
        var logScopes = new Dictionary<int, IScopedLog>();
        IScopedLog? lastLog = null;
        var lastLogLevel = MessageLevel.Error;

        this.context.Log.LogDebug("Executing docker " + args);
        var exitCode = await this.client.DockerAsync(
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
            if (text.StartsWith("#") && text.Contains(' ') && int.TryParse(text.AsSpan(1, text.IndexOf(' ') - 1), out var scopeNum))
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

    public Task<string> GetDigestAsync(string repositoryAndTag) => this.client.GetDigestAsync(repositoryAndTag, this.context.CancellationToken);
}
