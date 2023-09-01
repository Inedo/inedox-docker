using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations.Compose
{
    [Description("Builds, (re)creates, and optionally starts containers for a Docker Compose project.")]
    [ScriptAlias("Compose-Up")]
    public sealed class DockerComposeUpOperation : ComposeServicesOperationBase
    {
        protected override string Command => "up";

        public enum RecreateCondition
        {
            IfChanged,
            IfChangedRecursive,
            Always,
            Never
        }

        [DisplayName("Re-create containers")]
        [ScriptAlias("Recreate")]
        [DefaultValue(RecreateCondition.IfChanged)]
        public RecreateCondition Recreate { get; set; } = RecreateCondition.IfChanged;

        public enum BuildCondition
        {
            IfMissing,
            Always,
            Never
        }

        [DisplayName("Build images")]
        [ScriptAlias("Build")]
        [DefaultValue(BuildCondition.IfMissing)]
        public BuildCondition Build { get; set; } = BuildCondition.IfMissing;

        [DisplayName("Timeout (seconds)")]
        [ScriptAlias("Timeout")]
        [DefaultValue(10)]
        public int Timeout { get; set; } = 10;

        [DisplayName("Start containers")]
        [ScriptAlias("StartContainers")]
        [DefaultValue(true)]
        public bool StartContainers { get; set; } = true;

        public override Task ExecuteAsync(IOperationExecutionContext context)
        {
            return this.RunDockerComposeAsync(context,
                "--detach",
                "--no-color",
                this.Recreate == RecreateCondition.Always ? "--force-recreate" : null,
                this.Recreate == RecreateCondition.IfChangedRecursive ? "--always-recreate-deps" : null,
                this.Recreate == RecreateCondition.Never ? "--no-recreate" : null,
                this.Build == BuildCondition.Always ? "--build" : null,
                this.Build == BuildCondition.Never ? "--no-build" : null,
                "--remove-orphans",
                this.StartContainers ? null : "--no-start",
                "--timeout",
                this.Timeout.ToString()
            );
        }

        protected override string PrepareDescription(IOperationConfiguration config, RichDescription details)
        {
            if (Enum.TryParse<RecreateCondition>(config[nameof(Recreate)], out var recreate))
            {
                switch (recreate)
                {
                    case RecreateCondition.IfChanged:
                    default:
                        break;
                    case RecreateCondition.IfChangedRecursive:
                        details.AppendContent(" recreate containers ", new Hilite("if dependency changed"));
                        break;
                    case RecreateCondition.Always:
                        details.AppendContent(" recreate ", new Hilite("all"), " containers");
                        break;
                    case RecreateCondition.Never:
                        details.AppendContent(" ", new Hilite("do not"), " recreate existing containers");
                        break;
                }
            }

            if (Enum.TryParse<BuildCondition>(config[nameof(Build)], out var build))
            {
                switch (build)
                {
                    case BuildCondition.IfMissing:
                    default:
                        break;
                    case BuildCondition.Always:
                        details.AppendContent(" ", new Hilite("always"), " build images");
                        break;
                    case BuildCondition.Never:
                        details.AppendContent(" ", new Hilite("never"), " build images");
                        break;
                }
            }

            var timeout = AH.NullIf(AH.ParseInt(config[nameof(Timeout)]), 10);
            if (timeout.HasValue)
            {
                if (timeout == 0)
                {
                    details.AppendContent(" with no time limit");
                }
                else
                {
                    details.AppendContent(" with a time limit of ", new Hilite(timeout.ToString()), " seconds");
                }
            }

            if (string.Equals(config[nameof(StartContainers)], bool.FalseString, StringComparison.OrdinalIgnoreCase))
            {
                details.AppendContent(" (but do not start containers)");
            }

            return "Update";
        }
    }
}
