using Inedo.Extensibility.Operations;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations.Compose;

[ScriptAlias("Compose-Up")]
[Description("Builds, (re)creates, and optionally starts containers for a Docker Compose project.")]
public sealed class DockerComposeUpOperation : ComposeOperationBase
{
    protected override string Command => "up";

    [FieldEditMode(FieldEditMode.Multiline)]
    [ScriptAlias("Services")]
    [PlaceholderText("(all services)")]
    public IEnumerable<string>? Services { get; set; }

    public enum RecreateCondition
    {
        IfChanged,
        IfChangedRecursive,
        Always,
        Never
    }

    [Category("Options")]
    [DisplayName("Re-create containers")]
    [ScriptAlias("Recreate")]
    [DefaultValue(RecreateCondition.IfChanged)]
    public RecreateCondition Recreate { get; set; } = RecreateCondition.IfChanged;

    [Category("Options")]
    [ScriptAlias("RunInBackground")]
    [DisplayName("Run in background (--detach)")]
    [DefaultValue(true)]
    public bool RunInBackground { get; set; } = true;

    [Category("Options")]
    [DisplayName("Build images")]
    [ScriptAlias("Build")]
    [DefaultValue(BuildCondition.IfMissing)]
    public BuildCondition Build { get; set; } = BuildCondition.IfMissing;

    [Category("Options")]
    [DisplayName("Remove Orphans")]
    [ScriptAlias("RemoveOrphans")]
    [DefaultValue(false)]
    public bool RemoveOrphans { get; set; } = false;

    [Category("Options")]
    [DisplayName("Renew Anonymous Volumes")]
    [ScriptAlias("RenewAnonymousVolumes")]
    [DefaultValue(false)]
    public bool RenewAnonymousVolumes { get; set; } = false;

    [Category("Advanced")]
    [DisplayName("Timeout (seconds)")]
    [ScriptAlias("Timeout")]
    [DefaultValue(60)]
    public int Timeout { get; set; } = 60;

    public override Task ExecuteAsync(IOperationExecutionContext context)
    {
        return this.RunDockerComposeAsync(context,
                this.RunInBackground ? "--detach" : null,
                "--no-color",
                this.Recreate == RecreateCondition.Always ? "--force-recreate" : null,
                this.Recreate == RecreateCondition.IfChangedRecursive ? "--always-recreate-deps" : null,
                this.Recreate == RecreateCondition.Never ? "--no-recreate" : null,
                this.Build == BuildCondition.Always ? "--build" : null,
                this.Build == BuildCondition.Never ? "--no-build" : null,
                this.RemoveOrphans ? "--remove-orphans" : null,
                this.RenewAnonymousVolumes ? "--renew-anon-volumes" : null,
                "--timeout",
                this.Timeout.ToString()
        );
    }

    protected override string CreateExecutionParamenters(Func<string, string> escapeFunc, params string?[] args)
    {
        var commandArgs = base.CreateExecutionParamenters(escapeFunc, args);

        var services = this.Services?.ToArray() ?? [];
        if (services.Length > 0)
            commandArgs += string.Join(" ", ["--", .. services]);

        return commandArgs;
    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Runs docker compose up against ",
                new Hilite(config[nameof(ComposeFile)])
            )
        );
    }

    public enum BuildCondition
    {
        IfMissing,
        Always,
        Never
    }
}
