using System.Text;
using Inedo.Agents;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations;

[ScriptAlias("Exec")]
[ScriptAlias("Docker-Exec", Obsolete = true)]
[ScriptNamespace("Docker")]
[Description("Attaches and runs a command in an already running container")]
public sealed class DockerExecOperation : DockerOperation
{
    [DisplayName("Container name")]
    [ScriptAlias("ContainerName")]
    [DefaultValue("default (based on $DockerRepository)")]
    public string? ContainerName { get; set; }

    [Required]
    [DisplayName("Command")]
    [ScriptAlias("Command")]
    [PlaceholderText("eg. sh -c \"echo a && echo b\"")]
    public string? Command { get; set; }

    [DisplayName("Working directory in container")]
    [ScriptAlias("WorkDir")]
    public string? WorkDir { get; set; }

    [DisplayName("Log output (interactive)")]
    [ScriptAlias("Interactive")]
    [Description("Keep stdin open even if not attached")]
    [DefaultValue(true)]
    public bool Interactive { get; set; } = true;

    [DisplayName("Run in background (detach)")]
    [ScriptAlias("RunInBackground")]
    [Description("Detached mode: run command in the background")]
    [DefaultValue(false)]
    public bool RunInBackground { get; set; }

    [ScriptAlias("AdditionalArguments")]
    [DisplayName("Addtional arguments")]
    [Description("Additional arguments for the docker CLI exec command, such as --env key=value")]
    public string? AdditionalArguments { get; set; }

    public override async Task ExecuteAsync(IOperationExecutionContext context)
    {
        if (string.IsNullOrEmpty(this.ContainerName))
        {
            var repo = (await context.ExpandVariablesAsync("$DockerRepository")).AsString();
            if (string.IsNullOrWhiteSpace(repo))
                throw new ExecutionFailureException("ContainerName was not specified and $DockerRepository could not be resolved.");

            this.ContainerName = repo.Split('/').Last();
        }

        var remoteProcessExecuter = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();

        var args = new StringBuilder("exec ");
        if (this.RunInBackground)
            args.Append("--detach ");
        if (this.Interactive)
            args.Append("-i ");
        if (!string.IsNullOrWhiteSpace(this.WorkDir))
            args.Append($"--workdir {remoteProcessExecuter.EscapeArg(this.WorkDir)} ");

        if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
            args.Append($"{this.AdditionalArguments} ");

        args.Append($"{remoteProcessExecuter.EscapeArg(this.ContainerName)} {this.Command}");

        var client = await DockerClient.CreateAsync(this, context);
        await client.DockerAsync(args.ToString());
    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Execute ",
                new Hilite(config[nameof(Command)]),
                " on running container named ",
                new Hilite(config[nameof(ContainerName)])
            )
        );
    }
}
