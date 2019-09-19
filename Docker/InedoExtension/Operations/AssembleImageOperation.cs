using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;
using Inedo.Web.Plans.ArgumentEditors;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Assemble-Image")]
    [ScriptNamespace("Docker")]
    [DisplayName("Assemble Docker Image")]
    [Description("A simplified version of Build Docker Image for when the full power of a Dockerfile is not required.")]
    public sealed class AssembleImageOperation : DockerOperation
    {
        [Required]
        [Category("Image")]
        [ScriptAlias("Source")]
        [DisplayName("Container Source")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        public string ContainerSource { get; set; }
        [Required]
        [Category("Image")]
        [ScriptAlias("Repository")]
        [DisplayName("Repository name")]
        public string RepositoryName { get; set; }
        [Category("Image")]
        [ScriptAlias("Tag")]
        [DefaultValue("$ReleaseNumber-ci.$BuildNumber")]
        public string Tag { get; set; }

        [Required]
        [Category("Base")]
        [ScriptAlias("BaseSource")]
        [DisplayName("Container Source")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        public string BaseContainerSource { get; set; }
        [Required]
        [Category("Base")]
        [ScriptAlias("BaseRepository")]
        [DisplayName("Repository name")]
        public string BaseRepositoryName { get; set; }
        [Required]
        [Category("Base")]
        [ScriptAlias("BaseTag")]
        [DisplayName("Tag")]
        [DefaultValue("eg. latest")]
        public string BaseTag { get; set; }

        [Category("Contents")]
        [ScriptAlias("From")]
        [DisplayName("External path")]
        [PlaceholderText("$WorkingDirectory")]
        [FilePathEditor]
        public string SourceDirectory { get; set; }
        [Required]
        [Category("Contents")]
        [ScriptAlias("To")]
        [DisplayName("Internal path")]
        [PlaceholderText("eg. /opt/myapplication")]
        public string DestinationDirectory { get; set; }
        [Required]
        [Category("Contents")]
        [ScriptAlias("Cmd")]
        [DisplayName("Command")]
        [PlaceholderText("eg. ./myApplication")]
        public string Command { get; set; }
        [Category("Contents")]
        [ScriptAlias("Env")]
        [DisplayName("Environment variables")]
        [PlaceholderText("eg. %(VARIABLE_NAME: value, ...)")]
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; }
        [Category("Contents")]
        [ScriptAlias("Volumes")]
        [DisplayName("Data paths")]
        [FieldEditMode(FieldEditMode.Multiline)]
        public IEnumerable<string> Volumes { get; set; }

        [Category("Advanced")]
        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Addtional arguments")]
        [Description("Additional arguments for the docker CLI build command, such as --build-arg=ARG_NAME=value")]
        public string AdditionalArguments { get; set; }
        [Category("Advanced")]
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

            var baseId = new ContainerId(this.BaseContainerSource, this.BaseRepositoryName, this.BaseTag);
            if (!string.IsNullOrEmpty(this.ContainerSource))
                baseId = await this.PullAsync(context, baseId);
            else
                baseId = baseId.WithDigest(await this.ExecuteGetDigest(context, baseId.FullName));

            using (var stream = await fileOps.OpenFileAsync(fileOps.CombinePath(sourcePath, "Dockerfile"), FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(stream, InedoLib.UTF8Encoding, 8192, true))
            {
                writer.NewLine = "\n";

                await writer.WriteLineAsync($"FROM {baseId.FullerName}");
                foreach (var kv in this.EnvironmentVariables)
                {
                    await writer.WriteLineAsync($"ENV {kv.Key} {kv.Value}");
                }
                foreach (var vol in this.Volumes)
                {
                    await writer.WriteLineAsync($"VOLUME {vol}");
                }
                await writer.WriteLineAsync($"COPY . {this.DestinationDirectory}");
                await writer.WriteLineAsync($"WORKDIR {this.DestinationDirectory}");
                await writer.WriteLineAsync($"CMD {this.Command}");
                await writer.FlushAsync();
            }

            var containerId = new ContainerId(this.ContainerSource, this.RepositoryName, this.Tag);
            var escapeArg = GetEscapeArg(context);

            var args = $"build --force-rm --progress=plain --tag={escapeArg(containerId.FullName)} {this.AdditionalArguments} .";
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
                    "Assemble ",
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
