using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations.Compose
{
    [Description("Stops containers and removes containers, networks, volumes, and images created by Docker::Compose-Up.")]
    [ScriptAlias("Compose-Down")]
    public sealed class DockerComposeDownOperation : ComposeOperationBase
    {
        protected override string Command => "down";

        [DisplayName("Timeout (seconds)")]
        [ScriptAlias("Timeout")]
        [DefaultValue(10)]
        public int Timeout { get; set; } = 10;

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
            var shortDescription = new RichDescription(new Hilite("Remove"), " Docker Compose project ", new Hilite(config[nameof(ProjectName)]));

            var details = new RichDescription();
            var timeout = AH.NullIf(AH.ParseInt(config[nameof(Timeout)]), 10);
            if (timeout.HasValue)
            {
                if (timeout == 0)
                {
                    details.AppendContent("with no time limit");
                }
                else
                {
                    details.AppendContent("with a time limit of ", new Hilite(timeout.ToString()), " seconds");
                }
            }

            return new ExtendedRichDescription(shortDescription, details);
        }
    }
}
