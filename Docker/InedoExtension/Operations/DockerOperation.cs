using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;

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
            if (context.Agent.TryGetService<ILinuxFileOperationsExecuter>() == null)
            {
                return Utils.EscapeWindowsArg;
            }

            return Utils.EscapeLinuxArg;
        }

        protected async Task<ProcessOutput> ExecuteDockerAsync(IOperationExecutionContext context, string command, string arguments, string workingDirectory)
        {
            var output = new List<string>();
            var error = new List<string>();

            var exec = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
            using (var process = exec.CreateProcess(new RemoteProcessStartInfo { FileName = this.DockerExePath, Arguments = command + " " + arguments, WorkingDirectory = workingDirectory }))
            {
                process.OutputDataReceived += (s, e) => write(e.Data, output);
                process.ErrorDataReceived += (s, e) => write(e.Data, error);

                process.Start();

                await process.WaitAsync(context.CancellationToken);

                return new ProcessOutput(process.ExitCode.Value, output, error);
            }

            void write(string text, List<string> log)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lock (log)
                        log.Add(text);
                }
            }
        }

        protected async Task<string> ExecuteGetDigest(IOperationExecutionContext context, string tag)
        {
            var escapeArg = GetEscapeArg(context);
            var result = await this.ExecuteDockerAsync(context, "inspect", "--format='{{.Id}}' " + escapeArg(tag), null);
            if (result.ExitCode == 0 && result.Output.Count == 1)
                return result.Output[0].Trim();
            else
                return null;
        }

        public struct ContainerId
        {
            public string Source { get; }
            public string Name { get; }
            public string Tag { get; }
            public string Digest { get; }

            public ContainerId(string source, string name, string tag, string digest = null)
            {
                this.Source = source;
                this.Name = name ?? throw new ArgumentNullException(nameof(name));
                this.Tag = tag ?? throw new ArgumentNullException(nameof(name));
                this.Digest = digest;
            }

            public string FullName => GetContainerSourceServerName(this.Source) + this.Name + ":" + this.Tag;
            public string FullerName => this.FullName + AH.ConcatNE("@", this.Digest);

            public static implicit operator AttachedContainer(ContainerId containerId)
                => new AttachedContainer(containerId.Name, containerId.Tag, containerId.Digest, containerId.Source);

            public ContainerId WithDigest(string digest) => new ContainerId(this.Source, this.Name, this.Tag, digest);
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

        protected async Task LoginAsync(IOperationExecutionContext context, string containerSource)
        {
            if (string.IsNullOrEmpty(containerSource))
                return;

            var source = SDK.GetContainerSources().FirstOrDefault(cs => string.Equals(cs.Name, containerSource, StringComparison.OrdinalIgnoreCase));
            if (source == null)
                throw new InvalidOperationException("cannot find a container source named '" + containerSource + "'");

            if (string.IsNullOrEmpty(source.CredentialName))
                return;

            var escapeArg = GetEscapeArg(context);

            var credentials = (UsernamePasswordCredentials)ResourceCredentials.Create("UsernamePassword", source.CredentialName,
                (context as IStandardContext)?.EnvironmentId, (context as IStandardContext)?.ProjectId, true);

            var output = await this.ExecuteDockerAsync(context, "login", $"{escapeArg(GetServerName(source.RegistryUrl))} -u {escapeArg(credentials.UserName)} -p {escapeArg(AH.Unprotect(credentials.Password))}", null);
            if (output.ExitCode == 0)
                return;

            throw new ExecutionFailureException($"docker login returned code {output.ExitCode}:\n{string.Join("\n", output.Error)}");
        }
        protected async Task LogoutAsync(IOperationExecutionContext context, string containerSource)
        {
            if (string.IsNullOrEmpty(containerSource))
                return;

            var source = SDK.GetContainerSources().FirstOrDefault(cs => string.Equals(cs.Name, containerSource, StringComparison.OrdinalIgnoreCase));
            if (source == null)
                throw new InvalidOperationException("cannot find a container source named '" + containerSource + "'");

            if (string.IsNullOrEmpty(source.CredentialName))
                return;

            var escapeArg = GetEscapeArg(context);

            var output = await this.ExecuteDockerAsync(context, "logout", $"{escapeArg(GetServerName(source.RegistryUrl))}", null);
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

        protected async Task<ContainerId> PullAsync(IOperationExecutionContext context, ContainerId containerId)
        {
            if (string.IsNullOrEmpty(containerId.Source))
                throw new InvalidOperationException("cannot pull a container with no associated source");

            var escapeArg = GetEscapeArg(context);

            var exitCode = await this.ExecuteCommandLineAsync(context, new RemoteProcessStartInfo
            {
                FileName = this.DockerExePath,
                Arguments = "pull " + escapeArg(containerId.FullerName),
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

        protected static string GetContainerSourceServerName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            var source = SDK.GetContainerSources()
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

            if (source == null)
            {
                throw new ExecutionFailureException($"Container source \"{name}\" not found.");
            }

            return GetServerName(source.RegistryUrl);
        }

        protected static string GetServerName(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ExecutionFailureException("Invalid container registry URL: " + url);

            return $"{uri.Host}:{uri.Port}{uri.AbsolutePath.TrimEnd('/')}/";
        }
    }
}
