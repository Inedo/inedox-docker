using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.Credentials;
using Inedo.Extensions.SecureResources;
using UsernamePasswordCredentials = Inedo.Extensions.Credentials.UsernamePasswordCredentials;

namespace Inedo.Extensions.Docker.Operations
{
    [Tag("docker"), Tag("containers")]
    public abstract class DockerOperation : ExecuteOperation
    {
        protected DockerOperation()
        {
        }

        [Category("Advanced")]
        [DisplayName("Docker client path")]
        [ScriptAlias("DockerExePath")]
        [DefaultValue("$DockerExePath")]
        public string DockerExePath { get; set; }

        protected static Func<string, string> GetEscapeArg(IOperationExecutionContext context)
        {
            var procExec = context.Agent.GetService<IRemoteProcessExecuter>();
            return procExec.EscapeArg;
        }

        protected async Task<ProcessOutput> ExecuteDockerAsync(IOperationExecutionContext context, string command, string arguments, bool logOutput = false)
        {
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            await fileOps.CreateDirectoryAsync(context.WorkingDirectory);

            var output = new List<string>();
            var error = new List<string>();

            var exec = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
            using (var process = exec.CreateProcess(new RemoteProcessStartInfo { FileName = this.DockerExePath, Arguments = command + " " + arguments, WorkingDirectory = context.WorkingDirectory }))
            {
                process.OutputDataReceived += (s, e) => write(e.Data, output, logInfo: this.LogInformation);
                process.ErrorDataReceived += (s, e) => write(e.Data, error, logError: this.LogBuildError);

                process.Start();

                await process.WaitAsync(context.CancellationToken);

                return new ProcessOutput(process.ExitCode.Value, output, error);
            }

            void write(string text, List<string> log, Action<string> logInfo = null, Action<IOperationExecutionContext, string> logError = null)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lock (log)
                        log.Add(text);
                    if(logOutput)
                    {
                        logInfo?.Invoke(text);
                        logError?.Invoke(context, text);
                    }
                }
            }
        }

        protected async Task<string> ExecuteGetDigest(IOperationExecutionContext context, string tag)
        {
            var escapeArg = GetEscapeArg(context);
            var result = await this.ExecuteDockerAsync(context, "inspect", "--format='{{.Id}}' " + escapeArg(tag));
            if (result.ExitCode == 0 && result.Output.Count == 1)
                return result.Output[0].Trim();
            else
                return null;
        }

        private readonly Dictionary<int, IScopedLog> LogScopes = new Dictionary<int, IScopedLog>();
        private IScopedLog LastLog = null;
        private MessageLevel LastLogLevel = MessageLevel.Error;

        protected void LogBuildError(IOperationExecutionContext context, string text)
        {
            if (text.StartsWith("#") && text.Contains(" ") && int.TryParse(text.Substring(1, text.IndexOf(' ') - 1), out var scopeNum))
            {
                var message = text.Substring(text.IndexOf(' ') + 1);
                var firstWord = message.Substring(0, Math.Max(message.IndexOf(' '), 0));

                bool finished = false;
                MessageLevel level;
                if (decimal.TryParse(firstWord, out _))
                {
                    level = MessageLevel.Debug;
                    message = message.Substring(message.IndexOf(' ') + 1).TrimEnd('\r');
                    message = message.Substring(message.LastIndexOf('\r') + 1);
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

                if (this.LogScopes.TryGetValue(scopeNum, out var logScope))
                {
                    logScope.Log(level, message);
                    this.LastLog = logScope;
                }
                else
                {
                    logScope = context.Log.CreateNestedLog($"{scopeNum}. {message}");
                    this.LogScopes[scopeNum] = logScope;
                    this.LastLog = logScope;
                }

                if (finished)
                {
                    this.LastLog.Dispose();
                    this.LastLog = null;
                }

                this.LastLogLevel = level;
            }
            else
            {
                // a continuation of the previous non-build-process message
                this.Log(this.LastLogLevel, text.TrimEnd('\r'));
            }
        }

        public struct ContainerId
        {
            public string Source { get; }
            public string Prefix { get; }
            public string Name { get; }
            public string Tag { get; }
            public string Digest { get; }

            public ContainerId(string source, string prefix, string name, string tag, string digest = null)
            {
                this.Source = source?.TrimEnd('/');
                this.Prefix = prefix?.TrimEnd('/');
                this.Name = name ?? throw new ArgumentNullException(nameof(name));
                this.Tag = tag ?? throw new ArgumentNullException(nameof(name));
                this.Digest = digest;
            }

            public string FullName => (this.Prefix + "/" + this.Name + ":" + this.Tag).ToLower();

            public static implicit operator AttachedContainer(ContainerId containerId) => new AttachedContainer(containerId.Name, containerId.Tag, containerId.Digest, containerId.Source);

            public ContainerId WithDigest(string digest) => new ContainerId(this.Source, this.Prefix, this.Name, this.Tag, digest);
        }

        protected async Task AttachToBuildAsync(IOperationExecutionContext context, ContainerId containerId)
        {
            var containerManager = await context.TryGetServiceAsync<IContainerManager>();
            await containerManager.AttachContainerToBuildAsync(containerId, context.CancellationToken);
        }
        protected async Task DeactivateAttachedAsync(IOperationExecutionContext context, ContainerId containerId)
        {
            var containerManager = await context.TryGetServiceAsync<IContainerManager>();
            await containerManager.DeactivateContainerAsync(containerId.Name, containerId.Tag, containerId.Source);
        }

        protected async Task LoginAsync(IOperationExecutionContext context, string containerSource, bool logOutput = false)
        {
            if (string.IsNullOrEmpty(containerSource))
                return;

            var source = (ContainerSource)SecureResource.Create(containerSource, (IResourceResolutionContext)context);
            var creds = source.GetCredentials((ICredentialResolutionContext)context);
            if (creds == null)
                return;

            var userpass = source.GetCredentials((ICredentialResolutionContext)context) as UsernamePasswordCredentials;
            if (userpass == null)
                userpass = new UsernamePasswordCredentials { UserName = "api", Password = ((TokenCredentials)creds).Token };

            var escapeArg = GetEscapeArg(context);
            var output = await this.ExecuteDockerAsync(context, "login", $"{escapeArg(source.RegistryPrefix)} -u {escapeArg(userpass.UserName)} -p {escapeArg(AH.Unprotect(userpass.Password))}", logOutput: logOutput);
            if (output.ExitCode == 0)
                return;

            throw new ExecutionFailureException($"docker login returned code {output.ExitCode}:\n{string.Join("\n", output.Error)}");
        }
        protected async Task LogoutAsync(IOperationExecutionContext context, string containerSource, bool logOutput = false)
        {
            if (string.IsNullOrEmpty(containerSource))
                return;
            var source = (ContainerSource)SecureResource.Create(containerSource, (IResourceResolutionContext)context);
            var creds = source.GetCredentials((ICredentialResolutionContext)context);
            if (creds == null)
                return;

            var userpass = source.GetCredentials((ICredentialResolutionContext)context) as UsernamePasswordCredentials;
            if (userpass == null)
                userpass = new UsernamePasswordCredentials { UserName = "api", Password = ((TokenCredentials)creds).Token };

            var escapeArg = GetEscapeArg(context);
            var output = await this.ExecuteDockerAsync(context, "logout", $"{escapeArg(source.RegistryPrefix)}", logOutput: logOutput);
            if (output.ExitCode == 0)
                return;

            throw new ExecutionFailureException($"docker logout returned code {output.ExitCode}:\n{string.Join("\n", output.Error)}");
        }

        protected async Task PushAsync(IOperationExecutionContext context, ContainerId containerId)
        {
            if (string.IsNullOrEmpty(containerId.Source))
                throw new InvalidOperationException("cannot push a container with no associated source");

            var escapeArg = GetEscapeArg(context);
            var exitCode = await this.ExecuteCommandLineAsync(context, new RemoteProcessStartInfo
            {
                FileName = this.DockerExePath,
                Arguments = "push " + escapeArg(containerId.FullName),
                WorkingDirectory = context.WorkingDirectory
            });

            if (exitCode != 0)
                throw new ExecutionFailureException($"docker push returned code {exitCode}");
        }

        protected async Task RemoveAsync(IOperationExecutionContext context, ContainerId containerId)
        {
            if (string.IsNullOrEmpty(containerId.Source))
                throw new InvalidOperationException("cannot push a container with no associated source");

            var escapeArg = GetEscapeArg(context);
            var exitCode = await this.ExecuteCommandLineAsync(context, new RemoteProcessStartInfo
            {
                FileName = this.DockerExePath,
                Arguments = "rmi " + escapeArg(containerId.FullName),
                WorkingDirectory = context.WorkingDirectory
            });

            if (exitCode != 0)
                throw new ExecutionFailureException($"docker rmi returned code {exitCode}");
        }

        protected async Task<ContainerId> PullAsync(IOperationExecutionContext context, ContainerId containerId)
        {
            if (string.IsNullOrEmpty(containerId.Source))
                throw new InvalidOperationException("cannot pull a container with no associated source");

            var escapeArg = GetEscapeArg(context);

            var exitCode = await this.ExecuteCommandLineAsync(context, new RemoteProcessStartInfo
            {
                FileName = this.DockerExePath,
                Arguments = "pull " + escapeArg(containerId.FullName),
                WorkingDirectory = context.WorkingDirectory
            });

            if (exitCode != 0)
                throw new ExecutionFailureException($"docker pull returned code {exitCode}");

            var digest = await this.ExecuteGetDigest(context, containerId.FullName);
            return containerId.WithDigest(digest);
        }

        protected override void LogProcessError(string text) => this.LogDebug(text);

        protected sealed class ProcessOutput
        {
            public ProcessOutput(int exitCode, List<string> output, List<string> error)
            {
                this.ExitCode = exitCode;
                this.Output = output;
                this.Error = error;
            }

            public int ExitCode { get; }
            public List<string> Output { get; }
            public List<string> Error { get; }
        }
    }
}
