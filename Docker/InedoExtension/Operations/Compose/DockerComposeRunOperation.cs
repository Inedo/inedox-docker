using Inedo.Extensibility.Operations;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations.Compose;

[Description("Starts a new container for the specified service and runs a command.")]
[ScriptAlias("Compose-Run")]
public sealed class DockerComposeRunOperation : ComposeOperationBase
{
    protected override string Command => "run";

    [FieldEditMode(FieldEditMode.Multiline)]
    [ScriptAlias("Service")]
    [Required]
    public string Service { get; set; }

    [DisplayName("Working directory in container")]
    [ScriptAlias("WorkDir")]
    public string WorkDir { get; set; }

    [Required]
    [DisplayName("Exec Command")]
    [ScriptAlias("ExecCommand")]
    [PlaceholderText("eg. sh -c \"echo a && echo b\"")]
    public string ExecCommand { get; set; }

    [Category("Options")]
    [ScriptAlias("RunInteractively")]
    [DisplayName("Run Interactively (--interactive=true)")]
    [DefaultValue(true)]
    public bool Interactive { get; set; } = true;

    [Category("Options")]
    [DisplayName("Remove Orphans")]
    [ScriptAlias("RemoveOrphans")]
    [DefaultValue(false)]
    public bool RemoveOrphans { get; set; } = false;


    [Category("Options")]
    [ScriptAlias("RemoveContainer")]
    [DisplayName("Remove Container When Complete (--rm)")]
    [DefaultValue(true)]
    public bool RemoveContainer { get; set; } = true;

    [Category("Options")]
    [ScriptAlias("RunInBackground")]
    [DisplayName("Run in background (--detach)")]
    [DefaultValue(true)]
    public bool RunInBackground { get; set; } = false;

    public override Task ExecuteAsync(IOperationExecutionContext context)
    {
        return this.RunDockerComposeAsync(context,
            this.RunInBackground ? "--detach" : null,
            $"--interactive={this.Interactive.ToString().ToLower()}",
            this.RemoveContainer ? "--rm" : null,
            this.RemoveOrphans ? "--remove-orphans" : null
        );
    }

    protected override string CreateExecutionParamenters(Func<string, string> escapeFunc, params string[] args)
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
                " on a new container based on service ",
                new Hilite(config[nameof(Service)]),
                " in ",
                new Hilite(config[nameof(ComposeFile)])
            )
        );
    }
}
