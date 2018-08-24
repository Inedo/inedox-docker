using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations.Compose
{
    public abstract class ComposeServicesOperationBase : ComposeOperationBase
    {
        [FieldEditMode(FieldEditMode.Multiline)]
        [ScriptAlias("Services")]
        [PlaceholderText("(all services)")]
        public IEnumerable<string> Services { get; set; }

        protected override Task RunDockerComposeAsync(IOperationExecutionContext context, IEnumerable<string> args)
        {
            if (this.Services?.Any() == true)
            {
                args = args.Concat(new[] { "--" }).Concat(this.Services);
            }

            return base.RunDockerComposeAsync(context, args);
        }

        protected sealed override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var services = config[nameof(Services)].AsEnumerable();
            var details = new RichDescription();
            var verb = this.PrepareDescription(config, details);
            var shortDescription = new RichDescription(
                new Hilite(verb), " ",
                services.Any() ? (object)new ListHilite(services) : new Hilite("all services"),
                " for ", new Hilite(config[nameof(ProjectName)])
            );

            return new ExtendedRichDescription(shortDescription, details);
        }

        protected abstract string PrepareDescription(IOperationConfiguration config, RichDescription details);
    }
}
