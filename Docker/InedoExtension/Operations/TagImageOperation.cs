using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Extensions.SecureResources;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Tag-Image")]
    [ScriptNamespace("Docker")]
    [DisplayName("Tag Docker Image")]
    [Description("Applies a new tag to a Docker image in the specified container source.")]
    public sealed class TagImageOperation : DockerOperation
    {
        [Category("Legacy")]
        [ScriptAlias("RepositoryName")]
        [DisplayName("Source Repository name")]
        public string RepositoryName { get; set; }
        [Required]
        [Category("Source")]
        [ScriptAlias("OriginalTag")]
        [DisplayName("Original tag")]
        public string OriginalTag { get; set; }
        [Category("Source")]
        [ScriptAlias("Repository")]
        [ScriptAlias("Source")]
        [DisplayName("Repository")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        [DefaultValue("$DockerRepository")]
        public string DockerRepository { get; set; }

        [Category("Source")]
        [ScriptAlias("DeactivateOriginalTag")]
        [DisplayName("Remove from build")]
        [DefaultValue(true)]
        public bool DeactivateOriginalTag { get; set; } = true;

        [Category("Legacy")]
        [ScriptAlias("NewRepositoryName")]
        [DisplayName("New Repository name")]
        [PlaceholderText("(same as original repository name)")]
        public string NewRepositoryName { get; set; }
        [Required]
        [Category("Destination")]
        [ScriptAlias("NewTag")]
        [DisplayName("New tag")]
        public string NewTag { get; set; }
        [Category("Destination")]
        [ScriptAlias("NewRepository")]
        [ScriptAlias("NewSource")]
        [DisplayName("New Repository")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        [PlaceholderText("(same as original container source)")]
        public string NewDockerRepository { get; set; }
        [Category("Destination")]
        [ScriptAlias("AttachToBuild")]
        [DisplayName("Attach to build")]
        [DefaultValue(true)]
        public bool AttachToBuild { get; set; } = true;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            await this.LoginAsync(context, this.DockerRepository);
            try
            {
                var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
                await fileOps.CreateDirectoryAsync(context.WorkingDirectory);

                this.NewRepositoryName = AH.CoalesceString(this.NewRepositoryName, this.RepositoryName);
                if (string.IsNullOrWhiteSpace(this.NewDockerRepository))
                {
                    if (this.NewDockerRepository == null)
                        this.NewDockerRepository = this.DockerRepository;
                    else
                        this.NewDockerRepository = null;
                }
                var containerSource = (DockerRepository)SecureResource.Create(this.DockerRepository, (IResourceResolutionContext)context);
                containerSource = VerifyRepository(containerSource, this.RepositoryName);
                var oldContainerId = new ContainerId(this.DockerRepository, containerSource.GetFullRepository((ICredentialResolutionContext)context), this.OriginalTag);

                var newContainerSource = (DockerRepository)SecureResource.Create(this.NewDockerRepository, (IResourceResolutionContext)context);
                newContainerSource = VerifyRepository(newContainerSource, this.NewRepositoryName);
                var newContainerId = new ContainerId(this.NewDockerRepository, newContainerSource.GetFullRepository((ICredentialResolutionContext)context), this.NewTag);

                if (!string.IsNullOrEmpty(this.DockerRepository))
                    oldContainerId = await this.PullAsync(context, oldContainerId);
                else
                    oldContainerId = oldContainerId.WithDigest(await this.ExecuteGetDigest(context, oldContainerId.FullName));

                newContainerId = newContainerId.WithDigest(oldContainerId.Digest);

                var escapeArg = GetEscapeArg(context);

                int result = await this.ExecuteCommandLineAsync(
                    context,
                    new RemoteProcessStartInfo
                    {
                        FileName = this.DockerExePath,
                        Arguments = $"tag {escapeArg(oldContainerId.FullName)} {escapeArg(newContainerId.FullName)}"
                    }
                );
                if (result != 0)
                {
                    this.LogError("Docker exited with code " + result);
                    return;
                }

                if (!string.IsNullOrEmpty(this.NewDockerRepository))
                {
                    await this.PushAsync(context, newContainerId);
                }

                if (this.AttachToBuild)
                    await this.AttachToBuildAsync(context, newContainerId);

                if (this.DeactivateOriginalTag)
                    await this.DeactivateAttachedAsync(context, oldContainerId);
            }
            finally
            {
                await this.LogoutAsync(context, this.DockerRepository);
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Tag ",
                    new Hilite(config[nameof(DockerRepository)] + ":" + config[nameof(OriginalTag)]),
                    " as ",
                     new Hilite(AH.CoalesceString(config[nameof(DockerRepository)], config[nameof(NewDockerRepository)]) + ":" + config[nameof(NewTag)])
                )
            );
        }
    }
}
