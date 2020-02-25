using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Extensions.SecureResources;
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
            await this.LoginAsync(context, this.ContainerSource);
            try
            {
                var procExec = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
                var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
                await fileOps.CreateDirectoryAsync(context.WorkingDirectory);
                var sourcePath = context.ResolvePath(this.SourceDirectory);
                await fileOps.CreateDirectoryAsync(sourcePath);
                var dockerfilePath = fileOps.CombinePath(sourcePath, "Dockerfile");

                if (!string.IsNullOrWhiteSpace(this.DockerfileTemplate))
                {
                    var item = SDK.GetRaftItem(RaftItemType.TextTemplate, this.DockerfileTemplate, context);
                    if (item == null)
                    {
                        this.LogError($"Text template \"{this.DockerfileTemplate}\" not found.");
                        return;
                    }

                    var dockerfileText = await context.ApplyTextTemplateAsync(item.Content, this.TemplateArguments != null ? new Dictionary<string, RuntimeValue>(this.TemplateArguments) : null);

                    await fileOps.WriteAllTextAsync(dockerfilePath, dockerfileText, InedoLib.UTF8Encoding);
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
                    //["DOCKER_BUILDKIT"] = "1"
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
