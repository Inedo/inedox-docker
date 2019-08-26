using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Build-Image")]
    [ScriptNamespace("Docker")]
    [DisplayName("Build Docker Image")]
    [Description("Builds a Docker image.")]
    public sealed class BuildImageOperation : DockerOperation
    {
        [ScriptAlias("DockerfileAsset")]
        [DisplayName("Dockerfile text template")]
        public string DockerfileTemplate { get; set; }
        [ScriptAlias("TemplateArguments")]
        [DisplayName("Addtional template arguments")]
        public IDictionary<string, RuntimeValue> TemplateArguments { get; set; }
        [ScriptAlias("From")]
        [DisplayName("From")]
        [PlaceholderText("$WorkingDirectory")]
        public string SourceDirectory { get; set; }
        [Required]
        [ScriptAlias("Repository")]
        [DisplayName("Repository name")]
        public string RepositoryName { get; set; }
        [Required]
        [ScriptAlias("Tag")]
        public string Tag { get; set; }
        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Addtional arguments")]
        public string AdditionalArguments { get; set; }
        [ScriptAlias("AttachToBuild")]
        [DisplayName("Attach to build")]
        [DefaultValue(true)]
        public bool AttachToBuild { get; set; } = true;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var sourcePath = context.ResolvePath(this.SourceDirectory);
            await fileOps.CreateDirectoryAsync(sourcePath);

            if (!string.IsNullOrWhiteSpace(this.DockerfileTemplate))
            {
                string templateText;

                using (var raft = await context.OpenRaftAsync(null, OpenRaftOptions.ReadOnly | OpenRaftOptions.OptimizeLoadTime))
                {
                    using (var stream = await raft.OpenRaftItemAsync(RaftItemType.TextTemplate, this.DockerfileTemplate, FileMode.Open, FileAccess.Read))
                    {
                        if (stream == null)
                        {
                            this.LogError($"Text template \"{this.DockerfileTemplate}\" not found.");
                            return;
                        }

                        using (var reader = new StreamReader(stream, InedoLib.UTF8Encoding))
                        {
                            templateText = await reader.ReadToEndAsync();
                        }
                    }
                }

                var dockerfileText = await context.ApplyTextTemplateAsync(templateText, this.TemplateArguments != null ? new Dictionary<string, RuntimeValue>(this.TemplateArguments) : null);

                var dockerfilePath = fileOps.CombinePath(sourcePath, "Dockerfile");

                await fileOps.WriteAllTextAsync(dockerfilePath, dockerfileText, InedoLib.UTF8Encoding);
            }

            var args = $"build --force-rm --progress=plain --tag={this.RepositoryName}:{this.Tag} {this.AdditionalArguments} .";
            this.LogDebug("Executing docker " + args);

            await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = this.DockerExePath,
                    Arguments = args,
                    WorkingDirectory = sourcePath,
                    EnvironmentVariables =
                    {
                        ["DOCKER_BUILDKIT"] = "1"
                    }
                }
            );

            if (this.AttachToBuild)
                await this.AttachToBuildAsync(context, this.RepositoryName, this.Tag);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Build ",
                    new Hilite(config[nameof(RepositoryName)] + ":" + config[nameof(Tag)]),
                    " Docker image"
                ),
                new RichDescription(
                    "from ",
                    new DirectoryHilite(config[nameof(SourceDirectory)])
                )
            );
        }
    }
}
