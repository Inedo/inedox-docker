using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations.Compose;

[Description("Stops containers and removes containers, networks, volumes, and images created by Docker::Compose-Up.")]
[ScriptAlias("Compose-Down")]
public sealed class DockerComposeDownOperation : ComposeOperationBase
{
    protected override string Command => "down";

    [Category("Advanced")]
    [DisplayName("Timeout (seconds)")]
    [ScriptAlias("Timeout")]
    [DefaultValue(60)]
    public int Timeout { get; set; } = 60;

    public override Task ExecuteAsync(IOperationExecutionContext context)
    {
        return this.RunDockerComposeAsync(context,
            "--remove-orphans",
            "--timeout",
            this.Timeout.ToString()
        );
    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Runs docker compose down against ",
                new Hilite(config[nameof(ComposeFile)])
            )
        );
    }
}
