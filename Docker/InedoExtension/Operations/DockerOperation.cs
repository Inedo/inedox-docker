using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations
{
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
            var result = await this.ExecuteDockerAsync(context, "inspect", "--format='{{.Id}}' " + tag, null);
            if (result.ExitCode == 0 && result.Output.Count == 1)
                return result.Output[0].Trim();
            else
                return null;
        }

        protected async Task AttachToBuildAsync(IOperationExecutionContext context, string name, string tag, string source = null)
        {
            var digest = await this.ExecuteGetDigest(context, $"{name}:{tag}");
            this.LogDebug("Image digest: " + digest);

            var containerManager = await context.TryGetServiceAsync<IContainerManager>();
            await containerManager.AttachContainerToBuildAsync(
                new AttachedContainer(name, tag, digest, source),
                context.CancellationToken
            );
        }
        protected async Task DeactivateAttachedAsync(IOperationExecutionContext context, string name, string tag, string source = null)
        {
            var containerManager = await context.TryGetServiceAsync<IContainerManager>();
            await containerManager.DeactivateContainerAsync(name, tag, source);
        }

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
