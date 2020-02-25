using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
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
        [Required]
        [Category("Source")]
        [ScriptAlias("Repository")]
        [DisplayName("Repository name")]
        public string RepositoryName { get; set; }
        [Required]
        [Category("Source")]
        [ScriptAlias("OriginalTag")]
        [DisplayName("Original tag")]
        public string OriginalTag { get; set; }
        [Required]
        [Category("Source")]
        [ScriptAlias("Source")]
        [DisplayName("Container source")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        public string ContainerSource { get; set; }
        [Category("Source")]
        [ScriptAlias("DeactivateOriginalTag")]
        [DisplayName("Remove from build")]
        [DefaultValue(true)]
        public bool DeactivateOriginalTag { get; set; } = true;

        [Category("Destination")]
        [ScriptAlias("NewRepository")]
        [DisplayName("Repository name")]
        [PlaceholderText("(same as original repository name)")]
        public string NewRepositoryName { get; set; }
        [Required]
        [Category("Destination")]
        [ScriptAlias("NewTag")]
        [DisplayName("New tag")]
        public string NewTag { get; set; }
        [Category("Destination")]
        [ScriptAlias("NewSource")]
        [DisplayName("New container source")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        [PlaceholderText("(same as original container source)")]
        public string NewContainerSource { get; set; }
        [Category("Destination")]
        [ScriptAlias("AttachToBuild")]
        [DisplayName("Attach to build")]
        [DefaultValue(true)]
        public bool AttachToBuild { get; set; } = true;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            await this.LoginAsync(context, this.ContainerSource);
            try
            {
                var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
                await fileOps.CreateDirectoryAsync(context.WorkingDirectory);

                this.NewRepositoryName = AH.CoalesceString(this.NewRepositoryName, this.RepositoryName);
                if (string.IsNullOrWhiteSpace(this.NewContainerSource))
                {
                    if (this.NewContainerSource == null)
                        this.NewContainerSource = this.ContainerSource;
                    else
                        this.NewContainerSource = null;
                }
                var containerSource = (ContainerSource)SecureResource.Create(this.ContainerSource, (IResourceResolutionContext)context);
                var oldContainerId = new ContainerId(this.ContainerSource, containerSource?.RegistryPrefix, this.RepositoryName, this.OriginalTag);

                var newContainerSource = (ContainerSource)SecureResource.Create(this.NewContainerSource, (IResourceResolutionContext)context);
                var newContainerId = new ContainerId(this.NewContainerSource, newContainerSource?.RegistryPrefix, this.NewRepositoryName, this.NewTag);

                if (!string.IsNullOrEmpty(this.ContainerSource))
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

                if (!string.IsNullOrEmpty(this.NewContainerSource))
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
                await this.LogoutAsync(context, this.ContainerSource);
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Tag ",
                    new Hilite(config[nameof(RepositoryName)] + ":" + config[nameof(OriginalTag)]),
                    " as ",
                     new Hilite(AH.CoalesceString(config[nameof(RepositoryName)], config[nameof(NewRepositoryName)]) + ":" + config[nameof(NewTag)])
                )
            );
        }
    }
}
