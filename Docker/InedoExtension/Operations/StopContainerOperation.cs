using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations;

[ScriptAlias("Stop-Container")]
[ScriptNamespace("Docker")]
[Description("Stops a running Docker Container on a container host server.")]
public sealed class StopContainerOperation : DockerOperation
{
    [ScriptAlias("ContainerName")]
    [ScriptAlias("Container")]
    [DisplayName("Container name")]
    [PlaceholderText("default (based on $DockerRepository)")]
    [DefaultValue("$DockerRepository")]
    public string? ContainerName { get; set; }
    [ScriptAlias("Remove")]
    [DisplayName("Remove after stop")]
    [DefaultValue(true)]
    public bool Remove { get; set; } = true;
    [DefaultValue(false)]
    [ScriptAlias("FailIfContainerDoesNotExist")]
    [DisplayName("Fail if container does not exist")]
    public bool FailIfContainerDoesNotExist { get; set; }

    public override async Task ExecuteAsync(IOperationExecutionContext context)
    {
        if (string.IsNullOrEmpty(this.ContainerName))
        {
            var repo = (await context.ExpandVariablesAsync("$DockerRepository")).AsString();
            if (string.IsNullOrWhiteSpace(repo))
                throw new ExecutionFailureException("ContainerName was not specified and $DockerRepository could not be resolved.");

            this.ContainerName = repo.Split('/').Last();
        }
        
        var client = await DockerClient.CreateAsync(this, context);

        await client.DockerAsync($"stop {client.EscapeArg(this.ContainerName)}", failOnErrors: this.FailIfContainerDoesNotExist);

        if (this.Remove)
            await client.DockerAsync($"rm {client.EscapeArg(this.ContainerName)}", failOnErrors: this.FailIfContainerDoesNotExist);
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
