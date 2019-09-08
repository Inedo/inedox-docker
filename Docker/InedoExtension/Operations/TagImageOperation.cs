using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Tag-Image")]
    [ScriptNamespace("Docker")]
    [DisplayName("Retag Docker Image")]
    [Description("Applies a new tag a Docker image.")]
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
        public bool DeactivateOriginalTag { get; set; }

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
        public bool AttachToBuild { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            this.NewRepositoryName = AH.CoalesceString(this.NewRepositoryName, this.RepositoryName);
            if (string.IsNullOrEmpty(this.NewContainerSource))
            {
                if (this.NewContainerSource == null)
                    this.NewContainerSource = this.ContainerSource;
                else
                    this.NewContainerSource = null;
            }

            var sourceRootUrl = GetContainerSourceServerName(this.ContainerSource);
            var destRootUrl = GetContainerSourceServerName(this.NewContainerSource);

            var args = $"tag {sourceRootUrl}{this.RepositoryName}:{this.OriginalTag} {destRootUrl}{this.NewRepositoryName}:{this.NewTag}";

            int result = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = this.DockerExePath,
                    Arguments = args
                }
            );

            this.Log(result == 0 ? MessageLevel.Debug : MessageLevel.Error, "Docker exited with code " + result);
            if (result != 0)
                return;

            if (this.AttachToBuild)
                await this.AttachToBuildAsync(context, this.NewRepositoryName, this.NewTag, this.NewContainerSource);

            if (this.DeactivateOriginalTag)
                await this.DeactivateAttachedAsync(context, this.RepositoryName, this.OriginalTag, this.ContainerSource);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Retag ",
                    new Hilite(config[nameof(RepositoryName)] + ":" + config[nameof(OriginalTag)]),
                    " to ",
                     new Hilite(AH.CoalesceString(config[nameof(RepositoryName)], config[nameof(NewRepositoryName)]) + ":" + config[nameof(NewTag)])
                )
            );
        }
    }
}
