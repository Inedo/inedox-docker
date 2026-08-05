using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

#nullable enable

namespace Inedo.Extensions.Docker.Operations;

[ScriptAlias("Login")]
[ScriptNamespace("Docker")]
[Description("Executes Docker login on the selected Docker Repository.")]
public sealed class DockerLoginOperation : DockerOperation_ForTheNew
{
    [ScriptAlias("Repository")]
    [DisplayName("Repository")]
    [SuggestableValue(typeof(RepositoryResourceSuggestionProvider))]
    [DefaultValue("$DockerRepository")]
    public string? RepositoryResourceName { get; set; }

    public override async Task ExecuteAsync(IOperationExecutionContext context)
    {
        if (string.IsNullOrEmpty(this.RepositoryResourceName))
            throw new ExecutionFailureException($"A RepositoryResourceName or RepositoryUrl was not specified.");
        var repoResource = this.CreateRepository(context, this.RepositoryResourceName, null) 
                                ?? throw new ExecutionFailureException("Cannot find Docker Repository");

        var client = await DockerClientEx.CreateAsync(this, context);

        this.LogInformation($"Logging in to Docker for the {this.RepositoryResourceName} repository.");

        await client.LoginAsync(repoResource);

    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Login to the docker registry used by the ",
                new Hilite(config[nameof(RepositoryResourceName)]),
                " Docker image"
            )
        );
    }
}
