using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Stop-Container")]
    [ScriptNamespace("Docker")]
    [Description("Stops a Docker container.")]
    public sealed class StopContainerOperation : DockerOperation
    {
        [Required]
        [ScriptAlias("Container")]
        [DisplayName("Container name")]
        public string ContainerName { get; set; }
        [ScriptAlias("Delete")]
        [DisplayName("Delete container")]
        public bool Delete { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            this.LogDebug($"Executing docker stop {this.ContainerName}...");

            int result = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = this.DockerExePath,
                    Arguments = "stop " + this.ContainerName
                }
            );

            this.Log(result == 0 ? MessageLevel.Debug : MessageLevel.Error, "Docker exited with code " + result);

            if (this.Delete)
            {
                result = await this.ExecuteCommandLineAsync(
                    context,
                    new RemoteProcessStartInfo
                    {
                        FileName = this.DockerExePath,
                        Arguments = "rm " + this.ContainerName
                    }
                );

                this.Log(result == 0 ? MessageLevel.Debug : MessageLevel.Error, "Docker exited with code " + result);
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var desc = new RichDescription(
                "Stop ",
                new Hilite(config[nameof(ContainerName)]),
                " container"
            );

            if (string.Equals(config[nameof(Delete)], "true", StringComparison.OrdinalIgnoreCase))
                return new ExtendedRichDescription(desc, new RichDescription("and delete container"));
            else
                return new ExtendedRichDescription(desc);
        }
    }
}
