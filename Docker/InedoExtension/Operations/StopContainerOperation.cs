using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

#nullable enable

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Stop-Container")]
    [ScriptNamespace("Docker")]
    [DisplayName("Stop or Delete Docker Container")]
    [Description("Stops a running Docker Container on a container host server.")]
    public sealed class StopContainerOperation : DockerOperation_ForTheNew
    {
        [Required]
        [ScriptAlias("ContainerName")]
        [ScriptAlias("Container")]
        [DisplayName("Container name")]
        public string? ContainerName { get; set; }
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
            if (string.IsNullOrEmpty(this.ContainerName))
                throw new ExecutionFailureException($"A ContainerName was not specified.");
            
            var client = await DockerClientEx.CreateAsync(this, context);

            await client.DockerAsync($"stop {client.EscapeArg(this.ContainerName)}", failOnErrors: this.FailIfContinerDoesNotExist);

            if (this.Remove)
                await client.DockerAsync($"rm {client.EscapeArg(this.ContainerName)}", failOnErrors: this.FailIfContinerDoesNotExist);
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
