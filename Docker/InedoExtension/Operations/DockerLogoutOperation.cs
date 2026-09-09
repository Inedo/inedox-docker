using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations;

[ScriptAlias("Logout")]
[ScriptNamespace("Docker")]
[Description("Executes Docker logout on the selected Docker Repository.")]
public sealed class DockerLogoutOperation : DockerOperation
{
    [ScriptAlias("Repository")]
    [DisplayName("Repository")]
    [SuggestableValue(typeof(RepositoryResourceSuggestionProvider))]
    [DefaultValue("$DockerRepositoryResource")]
    public string? RepositoryResourceName { get; set; }

    public override async Task ExecuteAsync(IOperationExecutionContext context)
    {
        if (string.IsNullOrEmpty(this.RepositoryResourceName))
            throw new ExecutionFailureException($"A RepositoryResourceName or RepositoryUrl was not specified.");
        var repoResource = this.CreateRepository(context, this.RepositoryResourceName, null) 
                                ?? throw new ExecutionFailureException("Cannot find Docker Repository");

        var client = await DockerClient.CreateAsync(this, context);

        this.LogInformation($"Logging out of Docker for the {this.RepositoryResourceName} repository.");

        await client.LogoutAsync(repoResource);

    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Log out of the docker registry used by the ",
                new Hilite(config[nameof(RepositoryResourceName)]),
                " Docker image"
            )
        );
    }
}
