using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

#nullable enable
namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Tag-Image")]
    [ScriptNamespace("Docker")]
    [DisplayName("Tag Docker Image")]
    [Description("Applies a new tag to a Docker image in the specified container source.")]
    public sealed class TagImageOperation : DockerOperation
    {

        [Category("Source")]
        [ScriptAlias("Repository")]
        [ScriptAlias("Source")]
        [DisplayName("Repository")]
        [SuggestableValue(typeof(RepositoryRresourceSuggestionProvider))]
        [DefaultValue("$DockerRepository")]
        public string? RepositoryResourceName { get; set; }
        [Category("Source")]
        [ScriptAlias("OriginalTag")]
        [DisplayName("Original tag")]
        [DefaultValue("$DockerTag")]
        public string? OriginalTag { get; set; }

        [Category("Destination")]
        [ScriptAlias("NewRepository")]
        [ScriptAlias("NewSource")]
        [DisplayName("New Repository")]
        [SuggestableValue(typeof(RepositoryRresourceSuggestionProvider))]
        [PlaceholderText("(same as original container source)")]
        public string? NewRepositoryResourceName { get; set; }
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
        [Category("Legacy")]
        [ScriptAlias("NewRepositoryName")]
        [DisplayName("Override new repository name")]
        [PlaceholderText("(same as original repository name)")]
        public string? LegacyNewRepositoryName { get; set; }

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
                throw new ExecutionFailureException($"An OriginalTag was not specified.");
            if (string.IsNullOrEmpty(this.NewTag))
                throw new ExecutionFailureException($"A NewTag was not specified.");
            if (string.Equals(this.OriginalTag, this.NewTag, System.StringComparison.OrdinalIgnoreCase))
                throw new ExecutionFailureException($"OriginalTag and NewTag must be different.");

            if (string.IsNullOrEmpty(this.NewRepositoryResourceName))
                this.NewRepositoryResourceName = this.RepositoryResourceName;

            var originalRepoResource = this.CreateRepository(context, this.RepositoryResourceName, this.LegacyRepositoryName);
            var originalRepository = originalRepoResource.GetRepository(context);
            if (string.IsNullOrEmpty(originalRepository))
                throw new ExecutionFailureException($"Docker repository \"{this.RepositoryResourceName}\" has an unexpected name.");
            var originalRepositoryAndTag = $"{originalRepository}:{this.OriginalTag}";

            var newRepoResource = this.CreateRepository(context, this.NewRepositoryResourceName, this.LegacyNewRepositoryName);
            var newRepository = newRepoResource.GetRepository(context);
            if (string.IsNullOrEmpty(newRepository))
                throw new ExecutionFailureException($"Docker repository \"{this.NewRepositoryResourceName}\" has an unexpected name.");
            var newRepositoryAndTag = $"{newRepository}:{this.NewTag}";

            var client = await DockerClientEx.CreateAsync(this, context);
            var esc = client.EscapeArg;

            await client.LoginAsync(originalRepoResource);
            try
            {
                await client.DockerAsync($"pull {esc(originalRepositoryAndTag)}");
                await client.DockerAsync($"tag {esc(originalRepositoryAndTag)} {esc(newRepositoryAndTag)}");
                if (originalRepository != newRepository)
                {
                    await client.LogoutAsync();
                    await client.LoginAsync(newRepoResource);
                }
                await client.DockerAsync($"push {esc(newRepositoryAndTag)}");
            }
            finally
            {
                await client.LogoutAsync();
            }

            if (this.AttachToBuild)
            {
                var digest = await client.GetDigestAsync(newRepositoryAndTag);
                var containerManager = await context.TryGetServiceAsync<IContainerManager>()
                    ?? throw new ExecutionFailureException("Unable to get service IContainerManager to attach to build.");
                await containerManager.AttachContainerToBuildAsync(new(newRepository, this.NewTag, digest, this.NewRepositoryResourceName), context.CancellationToken);
            }

            if (this.DeactivateOriginalTag)
            {
                var containerManager = await context.TryGetServiceAsync<IContainerManager>()
                    ?? throw new ExecutionFailureException("Unable to get service IContainerManager to attach to build.");
                await containerManager.DeactivateContainerAsync(originalRepository, this.OriginalTag, this.RepositoryResourceName);
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Tag ",
                    new Hilite(config[nameof(RepositoryResourceName)] + ":" + config[nameof(OriginalTag)]),
                    " as ",
                     new Hilite(AH.CoalesceString(config[nameof(RepositoryResourceName)], config[nameof(NewRepositoryResourceName)]) + ":" + config[nameof(NewTag)])
                )
            );
        }
    }
}
