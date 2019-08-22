using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Tag-Image")]
    [ScriptNamespace("Docker")]
    [Description("Applies a new tag a Docker image.")]
    public sealed class TagImageOperation : DockerOperation
    {
        [Required]
        [ScriptAlias("Repository")]
        [DisplayName("Repository name")]
        public string RepositoryName { get; set; }
        [Required]
        [ScriptAlias("OriginalTag")]
        [DisplayName("Original tag")]
        public string OriginalTag { get; set; }
        [Required]
        [ScriptAlias("NewTag")]
        [DisplayName("New tag")]
        public string NewTag { get; set; }
        [Required]
        [ScriptAlias("Source")]
        [DisplayName("Container source")]
        public string ContainerSource { get; set; }
        [ScriptAlias("AttachToBuild")]
        [DisplayName("Attach to build")]
        [DefaultValue(true)]
        public bool AttachToBuild { get; set; } = true;
        [ScriptAlias("DeactivateOriginalTag")]
        [DisplayName("Deactivate original tag")]
        [DefaultValue(true)]
        public bool DeactivateOriginalTag { get; set; } = true;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var args = $"tag {this.RepositoryName}:{this.OriginalTag} {this.RepositoryName}:{this.NewTag}";

            int result = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = args
                }
            );

            this.Log(result == 0 ? MessageLevel.Debug : MessageLevel.Error, "Docker exited with code " + result);
            if (result != 0)
                return;

            if (this.AttachToBuild)
                await this.AttachToBuildAsync(context, this.RepositoryName, this.NewTag, this.ContainerSource);

            if (this.DeactivateOriginalTag)
                await this.DeactivateAttachedAsync(context, this.RepositoryName, this.NewTag, this.ContainerSource);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Tag ",
                    new Hilite(config[nameof(RepositoryName)] + ":" + config[nameof(OriginalTag)]),
                    " with ",
                     new Hilite(config[nameof(RepositoryName)] + ":" + config[nameof(NewTag)])
                )
            );
        }
    }
}
