using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

#nullable enable

namespace Inedo.Extensions.Docker.Operations;

[ScriptAlias("Execute-Command")]
[ScriptNamespace("Docker")]
[DisplayName("Executes a Docker Command")]
[Description("Logs into a Docker registry and runs a Docker CLI command on a container host server.")]
public sealed class DockerCommandOperation : DockerOperation
{
    [Required]
    [ScriptAlias("Command")]
    [DisplayName("Command")]
    [Description("Command for the Docker CLI command")]
    public string? Command { get; set; }

    [ScriptAlias("Repository")]
    [DisplayName("Repository")]
    [SuggestableValue(typeof(RepositoryResourceSuggestionProvider))]
    [DefaultValue("$DockerRepository")]
    [Description("This is only used for logging into a Docker registry. Leave blank to not login to a Docker registry.")]
    public string? RepositoryResourceName { get; set; }

    [ScriptAlias("Arguments")]
    [DisplayName("Arguments")]
    [Description("Arguments for the Docker CLI command, such as --env key=value")]
    public string? Arguments { get; set; }

    [Category("Legacy")]
    [ScriptAlias("RepositoryName")]
    [DisplayName("Override repository name")]
    [PlaceholderText("Do not override repository")]
    public string? LegacyRepositoryName { get; set; }

    public override async Task ExecuteAsync(IOperationExecutionContext context)
    {
        if (string.IsNullOrEmpty(this.Command))
            throw new ExecutionFailureException($"A Command was not specified.");
        
        var repoResource = string.IsNullOrWhiteSpace(this.RepositoryResourceName) ? this.CreateRepository(context, this.RepositoryResourceName, this.LegacyRepositoryName) : null;

        var client = await DockerClientEx.CreateAsync(this, context);

        if (repoResource != null)
            await client.LoginAsync(repoResource);
        try
        {
            await client.DockerAsync($"{this.Command} {this.Arguments}", true);
        }
        finally
        {
            if (repoResource != null)
                await client.LogoutAsync();
        }
    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Run docker ",
                new Hilite(config[nameof(Command)])
            ),
            new RichDescription(
                "using ",
                new Hilite(config[nameof(RepositoryResourceName)]),
                "."
            )
        );
    }
}
