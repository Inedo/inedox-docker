using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Credentials;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.ProGet;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations;

[ScriptAlias("Tag")]
[ScriptAlias("Tag-Image", Obsolete = true)]
[ScriptNamespace("Docker")]
[Description("Applies a new tag to a Docker image in a ProGet Docker feed.")]
public sealed class TagImageOperation : DockerOperation
{
    [Category("Source")]
    [ScriptAlias("Repository")]
    [ScriptAlias("Source")]
    [DisplayName("Repository")]
    [SuggestableValue(typeof(RepositoryResourceSuggestionProvider))]
    [DefaultValue("$DockerRepository")]
    public string? RepositoryResourceName { get; set; }
    [Category("Source")]
    [ScriptAlias("OriginalTag")]
    [DisplayName("Original tag")]
    [DefaultValue("$DockerTag")]
    public string? OriginalTag { get; set; }

    [Required]
    [Category("Destination")]
    [ScriptAlias("NewTag")]
    [DisplayName("New tag")]
    public string? NewTag { get; set; }

    [Category("Legacy")]
    [ScriptAlias("RepositoryName")]
    [DisplayName("Override repository name")]
    [PlaceholderText("Do not override repository")]
    public string? LegacyRepositoryName { get; set; }

    [Category("Advanced")]
    [ScriptAlias("AttachToBuild")]
    [DisplayName("Attach to build")]
    [DefaultValue(true)]
    public bool AttachToBuild { get; set; } = true;
    [Category("Advanced")]
    [ScriptAlias("DeactivateOriginalTag")]
    [DisplayName("Remove from build")]
    [DefaultValue(true)]
    public bool DeactivateOriginalTag { get; set; } = true;

    public override async Task ExecuteAsync(IOperationExecutionContext context)
    {
        if (string.IsNullOrEmpty(this.OriginalTag))
            throw new ExecutionFailureException("OriginalTag was not specified.");
        if (string.IsNullOrEmpty(this.NewTag))
            throw new ExecutionFailureException("NewTag was not specified.");

        var originalRepoResource = this.CreateRepository(context, this.RepositoryResourceName, this.LegacyRepositoryName);
        var originalRepository = originalRepoResource.GetRepository(context);
        if (string.IsNullOrEmpty(originalRepository))
             throw new ExecutionFailureException($"Docker repository \"{this.RepositoryResourceName}\" has an unexpected name.");

        if (originalRepoResource.GetCredentials(context) is not ProGetServiceCredentials pgCreds)
            throw new ExecutionFailureException("This operation requires a ProGet connection.");

        if (string.IsNullOrWhiteSpace(pgCreds.ServiceUrl))
            throw new ExecutionFailureException("ProGet connection is missing a URL.");

        ProGetClient client;
        if (!string.IsNullOrWhiteSpace(pgCreds.APIKey))
            client = new ProGetClient(pgCreds.ServiceUrl, pgCreds.APIKey);
        else if (!string.IsNullOrWhiteSpace(pgCreds.UserName) && !string.IsNullOrWhiteSpace(pgCreds.Password))
            client = new ProGetClient(pgCreds.ServiceUrl, pgCreds.UserName, pgCreds.Password);
        else
            client = new ProGetClient(pgCreds.ServiceUrl);

        var repoParts = originalRepository.Split('/', 3);
        if (repoParts.Length != 3)
            throw new ExecutionFailureException("Missing ProGet feed in repository name.");

        this.LogInformation($"Tagging {originalRepository}:{this.OriginalTag} as {this.NewTag}...");
        try
        {
            var res = await client.AddContainerImageTag2Async(repoParts[1], repoParts[2], this.NewTag, this.OriginalTag, cancellationToken: context.CancellationToken);
            switch (res.Status)
            {
                case AddTagResult.Exists:
                    this.LogInformation("Tag already exists; nothing to do.");
                    break;

                case AddTagResult.Created:
                    this.LogInformation("Tag created.");
                    break;

                case AddTagResult.Updated:
                    this.LogInformation("Existing tag updated.");
                    break;
            }

            if (this.AttachToBuild || this.DeactivateOriginalTag)
            {
                var containerManager = await context.TryGetServiceAsync<IContainerManager>()
                    ?? throw new ExecutionFailureException("Unable to get service IContainerManager to attach to build.");

                if (this.AttachToBuild)
                    await containerManager.AttachContainerToBuildAsync(new AttachedContainer(originalRepository, this.NewTag, res.Digest, this.RepositoryResourceName), context.CancellationToken);

                if (this.DeactivateOriginalTag)
                    await containerManager.DeactivateContainerAsync(originalRepository, this.OriginalTag, this.RepositoryResourceName);
            }
        }
        catch (ProGetApiException ex)
        {
            this.LogError(ex.Message);
        }
    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Tag ",
                new Hilite($"{config[nameof(RepositoryResourceName)]}:{config[nameof(OriginalTag)]}"),
                " as ",
                 new Hilite($"{config[nameof(RepositoryResourceName)]}:{config[nameof(NewTag)]}")
            )
        );
    }
}
