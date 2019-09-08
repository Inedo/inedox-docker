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
using Inedo.Web;
using Inedo.Web.Plans.ArgumentEditors;

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
        [FieldEditMode(FieldEditMode.Multiline)]
        [PlaceholderText("eg. %(name: value, ...)")]
        public IDictionary<string, RuntimeValue> TemplateArguments { get; set; }
        [ScriptAlias("From")]
        [DisplayName("From")]
        [PlaceholderText("$WorkingDirectory")]
        [FilePathEditor]
        public string SourceDirectory { get; set; }
        [Required]
        [ScriptAlias("Repository")]
        [DisplayName("Repository name")]
        public string RepositoryName { get; set; }
        [Required]
        [ScriptAlias("Tag")]
        [PlaceholderText("eg. $ReleaseNumber-ci.$BuildNumber")]
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

            var exitCode = await this.ExecuteCommandLineAsync(
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
            if (exitCode != 0)
            {
                this.LogError($"exit code: {exitCode}");
                return;
            }

            if (this.AttachToBuild)
                await this.AttachToBuildAsync(context, this.RepositoryName, this.Tag);
        }

        private readonly Dictionary<int, object> LogScopes = new Dictionary<int, object>();

        protected override void LogProcessError(string text)
        {
            if (text.StartsWith("#") && int.TryParse(text.Substring(1, text.IndexOf(' ') - 1), out var scopeNum))
            {
                var message = text.Substring(text.IndexOf(' ') + 1);
                var firstWord = message.Substring(0, message.IndexOf(' '));

                MessageLevel level;
                if (decimal.TryParse(firstWord, out var timeSpent))
                {
                    level = MessageLevel.Debug;
                    message = message.Substring(message.IndexOf(' ') + 1).TrimEnd('\r');
                    message = message.Substring(message.LastIndexOf('\r') + 1);
                    // TODO: log is from build process
                }
                else if (firstWord == "DONE")
                {
                    level = MessageLevel.Information;
                }
                else if (firstWord == "ERROR")
                {
                    level = MessageLevel.Error;
                }
                else
                {
                    level = MessageLevel.Information;
                }

                if (LogScopes.TryGetValue(scopeNum, out var logScope))
                {
                    // TODO: write to scoped log
                    this.Log(level, message);
                }
                else
                {
                    // TODO: create scoped log
                    this.Log(level, message);
                }
            }
            else
            {
                // a continuation of the previous non-build-process message
                this.LogError(text.TrimEnd('\r'));
            }
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
