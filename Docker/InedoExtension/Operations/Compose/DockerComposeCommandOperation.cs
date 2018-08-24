using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations.Compose
{
    [DisplayName("Run command in Docker Compose")]
    [Description("Runs an arbitrary docker-compose command.")]
    [ScriptAlias("Compose-Command")]
    public sealed class DockerComposeCommandOperation : ComposeOperationBase
    {
        [Category]
        [ScriptAlias("Args")]
        [DisplayName("Command arguments")]
        [FieldEditMode(FieldEditMode.Multiline)]
        public override IEnumerable<string> AddArgs { get; set; }

        public override Task ExecuteAsync(IOperationExecutionContext context)
        {
            return this.RunDockerComposeAsync(context);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription("Run docker-compose for ", new Hilite(config[nameof(ProjectName)])),
                new RichDescription("with arguments ", new ListHilite(this.AddArgs))
            );
        }
    }
}
