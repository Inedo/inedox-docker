using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Docker-Logout")]
    [ScriptNamespace("Docker")]
    [DisplayName("Docker Logout")]
    [Description("Log out of a docker registry.")]
    public sealed class DockerLogoutOperation : DockerOperation
    {
        [Required]
        [Category("Source")]
        [ScriptAlias("Source")]
        [DisplayName("Container source")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        public string ContainerSource { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            await this.LogoutAsync(context, this.ContainerSource, true);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Logout of ",
                    new Hilite(config[nameof(ContainerSource)])
                )
            );
        }
    }
}
