using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Docker-Login")]
    [ScriptNamespace("Docker")]
    [DisplayName("Docker Login")]
    [Description("Log in to of a docker registry.")]
    public sealed class DockerLoginOperation : DockerOperation
    {
        [Required]
        [Category("Source")]
        [ScriptAlias("Source")]
        [DisplayName("Container source")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        public string ContainerSource { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            await this.LoginAsync(context, this.ContainerSource, true);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Login to ",
                    new Hilite(config[nameof(ContainerSource)])
                )
            );
        }
    }
}
