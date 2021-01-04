using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
        [ScriptAlias("RepositoryName")]
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
        [ScriptAlias("BaseRepositoryName")]
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
        public string SourceDirectory { get; set; }
        [Category("Contents")]
        [ScriptAlias("To")]
        [DisplayName("Internal path")]
        [PlaceholderText("eg. /opt/myapplication")]
        public string DestinationDirectory { get; set; }
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
            await this.LoginAsync(context, this.ContainerSource);
            try
            {
                var procExec = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
                var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
                await fileOps.CreateDirectoryAsync(context.WorkingDirectory);
                var sourcePath = context.ResolvePath(this.SourceDirectory);
                await fileOps.CreateDirectoryAsync(sourcePath);

                var baseContainerSource = (ContainerSource)SecureResource.Create(this.BaseContainerSource, (IResourceResolutionContext)context);
                var baseId = new ContainerId(this.BaseContainerSource, baseContainerSource?.RegistryPrefix, this.BaseRepositoryName, this.BaseTag);

                if (!string.IsNullOrEmpty(this.ContainerSource))
                    baseId = await this.PullAsync(context, baseId);
                else
                    baseId = baseId.WithDigest(await this.ExecuteGetDigest(context, baseId.FullName));

                using (var stream = await fileOps.OpenFileAsync(fileOps.CombinePath(sourcePath, "Dockerfile"), FileMode.Create, FileAccess.Write))
                using (var writer = new StreamWriter(stream, InedoLib.UTF8Encoding, 8192, true))
                {
                    writer.NewLine = "\n";

                    await writer.WriteLineAsync($"FROM {baseId.FullName}");
                    foreach (var kv in this.EnvironmentVariables ?? new Dictionary<string, string>())
                    {
                        await writer.WriteLineAsync($"ENV {kv.Key} {kv.Value}");
                    }
                    foreach (var vol in this.Volumes ?? new List<string>())
                    {
                        await writer.WriteLineAsync($"VOLUME {vol}");
                    }
                    if (!string.IsNullOrWhiteSpace(this.DestinationDirectory))
                    {
                        await writer.WriteLineAsync($"COPY . {this.DestinationDirectory}");
                        await writer.WriteLineAsync($"WORKDIR {this.DestinationDirectory}");
                    }
                    if (!string.IsNullOrWhiteSpace(this.Command))
                    {
                        await writer.WriteLineAsync($"CMD {this.Command}");
                    }
                    await writer.FlushAsync();
                }

                var containerSource = (ContainerSource)SecureResource.Create(this.ContainerSource, (IResourceResolutionContext)context);
                var containerId = new ContainerId(this.ContainerSource, containerSource?.RegistryPrefix, this.RepositoryName, this.Tag);
                var escapeArg = GetEscapeArg(context);

                var args = $"build --force-rm --progress=plain --tag={escapeArg(containerId.FullName)} {this.AdditionalArguments} {escapeArg(sourcePath)}";
                this.LogDebug("Executing docker " + args);

                using (var process = procExec.CreateProcess(new RemoteProcessStartInfo
                {
                    FileName = this.DockerExePath,
                    Arguments = args,
                    WorkingDirectory = sourcePath,
                    EnvironmentVariables =
                    {
                       // ["DOCKER_BUILDKIT"] = "1"
                    }
                }))
                {
                    process.OutputDataReceived += (s, e) => this.LogInformation(e.Data);
                    process.ErrorDataReceived += (s, e) => this.LogBuildError(context, e.Data);

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
            finally
            {
                await this.LogoutAsync(context, this.ContainerSource);
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
