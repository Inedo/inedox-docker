using System;
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
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;
using Inedo.Web.Plans.ArgumentEditors;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Build-Image")]
    [ScriptNamespace("Docker")]
    [DisplayName("Build Docker Image")]
    [Description("Builds a Docker image using a text template and pushes it to a specified container source.")]
    public sealed class BuildImageOperation : DockerOperation
    {
        [Required]
        [ScriptAlias("DockerfileAsset")]
        [DisplayName("Dockerfile text template")]
        [PlaceholderText("Select text template...")]
        [SuggestableValue(typeof(TextTemplateSuggestionProvider))]
        public string DockerfileTemplate { get; set; }
        [ScriptAlias("TemplateArguments")]
        [DisplayName("Addtional template arguments")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [PlaceholderText("eg. %(name: value, ...)")]
        public IDictionary<string, RuntimeValue> TemplateArguments { get; set; }
        [Required]
        [ScriptAlias("Source")]
        [DisplayName("Container Source")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        public string ContainerSource { get; set; }
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
        [Description("Additional arguments for the docker CLI build command, such as --build-arg=ARG_NAME=value")]
        public string AdditionalArguments { get; set; }
        [ScriptAlias("AttachToBuild")]
        [DisplayName("Attach to build")]
        [DefaultValue(true)]
        public bool AttachToBuild { get; set; } = true;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var procExec = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
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

            var containerId = new ContainerId(this.ContainerSource, this.RepositoryName, this.Tag);

            var args = $"build --force-rm --progress=plain --tag={containerId.FullName} {this.AdditionalArguments} .";
            this.LogDebug("Executing docker " + args);

            using (var process = procExec.CreateProcess(new RemoteProcessStartInfo
            {
                FileName = this.DockerExePath,
                Arguments = args,
                WorkingDirectory = sourcePath,
                EnvironmentVariables =
                {
                    ["DOCKER_BUILDKIT"] = "1"
                }
            }))
            {
                process.OutputDataReceived += (s, e) => this.LogInformation(e.Data);
                process.ErrorDataReceived += (s, e) => this.LogBuildError(e.Data);

                process.Start();
                await process.WaitAsync(context.CancellationToken);

                if (process.ExitCode != 0)
                {
                    this.LogError($"exit code: {process.ExitCode ?? -1}");
                    return;
                }
            }

            var digest = await this.ExecuteGetDigest(context, containerId.FullName);
            containerId = containerId.WithDigest(digest);

            if (!string.IsNullOrEmpty(this.ContainerSource))
                await this.PushAsync(context, containerId);

            if (this.AttachToBuild)
                await this.AttachToBuildAsync(context, containerId);
        }

        private readonly Dictionary<int, object> LogScopes = new Dictionary<int, object>();
        private MessageLevel LastLogLevel = MessageLevel.Error;

        private void LogBuildError(string text)
        {
            if (text.StartsWith("#") && text.Contains(" ") && int.TryParse(text.Substring(1, text.IndexOf(' ') - 1), out var scopeNum))
            {
                var message = text.Substring(text.IndexOf(' ') + 1);
                var firstWord = message.Substring(0, Math.Max(message.IndexOf(' '), 0));

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

                if (this.LogScopes.TryGetValue(scopeNum, out var logScope))
                {
                    // TODO: write to scoped log
                    this.Log(level, message);
                }
                else
                {
                    // TODO: create scoped log
                    this.Log(level, message);
                }

                this.LastLogLevel = level;
            }
            else
            {
                // a continuation of the previous non-build-process message
                this.Log(this.LastLogLevel, text.TrimEnd('\r'));
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
