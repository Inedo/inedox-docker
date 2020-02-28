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
    [DisplayName("Stop or Delete Docker Container")]
    [Description("Stops a running Docker Container on a container host server.")]
    public sealed class StopContainerOperation : DockerOperation
    {
        [Required]
        [ScriptAlias("ContainerName")]
        [ScriptAlias("Container")]
        [DisplayName("Container name")]
        public string ContainerName { get; set; }
        [ScriptAlias("Remove")]
        [DisplayName("Remove after stop")]
        [DefaultValue(true)]
        public bool Remove { get; set; } = true;
        [DefaultValue(false)]
        [ScriptAlias("FailIfContainerDoesNotExist")]
        [DisplayName("Fail if container does not exist")]
        public bool FailIfContinerDoesNotExist { get; set; } = false;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            this.LogDebug($"Executing docker stop {this.ContainerName}...");

            var escapeArg = GetEscapeArg(context);

            int result = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = this.DockerExePath,
                    Arguments = "stop " + escapeArg(this.ContainerName)
                }
            );

            this.Log(result == 0 ? MessageLevel.Debug : (FailIfContinerDoesNotExist ? MessageLevel.Error : MessageLevel.Warning), "Docker exited with code " + result);

            if (this.Remove)
            {
                result = await this.ExecuteCommandLineAsync(
                    context,
                    new RemoteProcessStartInfo
                    {
                        FileName = this.DockerExePath,
                        Arguments = "rm " + escapeArg(this.ContainerName)
                    }
                );

                this.Log(result == 0 ? MessageLevel.Debug : (FailIfContinerDoesNotExist ? MessageLevel.Error : MessageLevel.Warning), "Docker exited with code " + result);
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var desc = new RichDescription(
                "Stop ",
                new Hilite(config[nameof(ContainerName)]),
                " container"
            );

            if (string.Equals(config[nameof(Remove)], "true", StringComparison.OrdinalIgnoreCase))
                return new ExtendedRichDescription(desc, new RichDescription("and delete container"));
            else
                return new ExtendedRichDescription(desc);
        }
    }
}
