using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

#nullable enable

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Stop-Container")]
    [ScriptNamespace("Docker")]
    [Description("Stops a running Docker Container on a container host server.")]
    public sealed class StopContainerOperation : DockerOperation_ForTheNew
    {
        [ScriptAlias("ContainerName")]
        [ScriptAlias("Container")]
        [DisplayName("Container name")]
        [DefaultValue("default (based on $DockerRepository)")]
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
            {
                var maybeVariable = context.TryGetVariableValue(new RuntimeVariableName("DockerRepository", RuntimeValueType.Scalar));
                if (maybeVariable == null)
                {
                    var maybeFunc = context.TryGetFunctionValue("DockerRepository");
                    if (maybeFunc == null)
                        throw new ExecutionFailureException($"A ContainerName was not specified and $DockerRepository could not be resolved.");
                    else
                        this.ContainerName = maybeFunc.Value.AsString()!.Split('/').Last();
                }
                else
                    this.ContainerName = maybeVariable.Value.AsString()!.Split('/').Last();
            }
            
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
