using Inedo.Extensibility.Operations;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations.Compose;

[ScriptAlias("Compose-Exec")]
[Description("Runs commands on currently running containers that were started using Docker::Compose-Up.")]
public sealed class DockerComposeExecOperation : ComposeOperationBase
{
    protected override string Command => "exec";

    [FieldEditMode(FieldEditMode.Multiline)]
    [ScriptAlias("Service")]
    [Required]
    public string? Service { get; set; }

    [DisplayName("Working directory in container")]
    [ScriptAlias("WorkDir")]
    public string? WorkDir { get; set; }

    [Required]
    [DisplayName("Exec Command")]
    [ScriptAlias("ExecCommand")]
    [PlaceholderText("eg. sh -c \"echo a && echo b\"")]
    public string? ExecCommand { get; set; }

    [Category("Options")]
    [ScriptAlias("RunInBackground")]
    [DisplayName("Run in background (--detach)")]
    [DefaultValue(true)]
    public bool RunInBackground { get; set; } = false;

    public override Task ExecuteAsync(IOperationExecutionContext context)
    {
        return this.RunDockerComposeAsync(context,
            this.RunInBackground ? "--detach" : null
        );
    }

    protected override string CreateExecutionParamenters(Func<string, string> escapeFunc, params string?[] args)
    {
        var command = base.CreateExecutionParamenters(escapeFunc, [..args, string.IsNullOrWhiteSpace(this.WorkDir) ? null : $"--workdir {escapeFunc(this.WorkDir)}"]);

        command += $"-- {this.Service} {this.ExecCommand}";

        return command;
    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Executes ",
                new Hilite(config[nameof(Command)]),
                " on the running service ",
                new Hilite(config[nameof(Service)]),
                " in ",
                new Hilite(config[nameof(ComposeFile)])
            )
        );
    }
}
