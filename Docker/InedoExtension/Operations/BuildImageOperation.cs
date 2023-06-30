using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Extensions.SecureResources;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Build-Image")]
    [ScriptNamespace("Docker")]
    [DisplayName("Build Docker Image")]
    [Description("Builds a Docker image using a text template and pushes it to a specified container source.")]
    public sealed class BuildImageOperation : DockerOperation
    {
        [ScriptAlias("DockerfileAsset")]
        [DisplayName("Dockerfile text template")]
        [PlaceholderText("Select text template...")]
        [SuggestableValue(typeof(DockerfileSuggestionProvider))]
        public string DockerfileTemplate { get; set; }
        [ScriptAlias("TemplateArguments")]
        [DisplayName("Addtional template arguments")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [PlaceholderText("eg. %(name: value, ...)")]
        public IDictionary<string, RuntimeValue> TemplateArguments { get; set; }
        [ScriptAlias("Repository")]
        [ScriptAlias("Source")]
        [DisplayName("Repository")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        [DefaultValue("$DockerRepository")]
        public string DockerRepository { get; set; }
        [ScriptAlias("From")]
        [DisplayName("From")]
        [PlaceholderText("$WorkingDirectory")]
        [FieldEditMode(FieldEditMode.ServerDirectoryPath)]
        public string SourceDirectory { get; set; }
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

        [Category("Advanced")]
        [ScriptAlias("RemoveAfterPush")]
        [DisplayName("Remove after pushing")]
        public bool RemoveAfterPush { get; set; }

        [Category("Advanced")]
        [ScriptAlias("DockerfileName")]
        [DisplayName("Dockerfiel Name")]
        [PlaceholderText("Dockerfile")]
        [DefaultValue("Dockerfile")]
        [Description("The name of the Dockerfile to use when building your image.")]
        public string DockerfileName { get; set; }

        [ScriptAlias("RepositoryName")]
        [DisplayName("Repository name")]
        [PlaceholderText("Use from Container Source")]
        [Category("Legacy")]
        public string RepositoryName { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            await this.LoginAsync(context, this.DockerRepository);
            try
            {
                var procExec = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
                var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
                await fileOps.CreateDirectoryAsync(context.WorkingDirectory);
                var sourcePath = context.ResolvePath(this.SourceDirectory);
                await fileOps.CreateDirectoryAsync(sourcePath);
                var dockerfilePath = fileOps.CombinePath(sourcePath, this.DockerfileName);

                if (!string.IsNullOrWhiteSpace(this.DockerfileTemplate))
                {
                    SDK.RaftItemInfo item = null;

#warning Updated to use actual enum type RaftItemType.BuildFile once upgraded to SDK 2.4
                    try
                    {
                        item = SDK.GetRaftItem((RaftItemType)11, this.DockerfileTemplate, context);
                    } catch { /* Error most likely RaftItemType does not exist */ }

                    if (item == null)
                        item = SDK.GetRaftItem(RaftItemType.TextTemplate, this.DockerfileTemplate, context);
                    if (item == null)
                    {
                        this.LogError($"Dockerfile \"{this.DockerfileTemplate}\" not found.");
                        return;
                    }

                    var dockerfileText = await context.ApplyTextTemplateAsync(item.Content, this.TemplateArguments != null ? new Dictionary<string, RuntimeValue>(this.TemplateArguments) : null);

                    await fileOps.WriteAllTextAsync(dockerfilePath, dockerfileText, InedoLib.UTF8Encoding);
                }

                var containerSource = (DockerRepository)SecureResource.Create(this.DockerRepository, (IResourceResolutionContext)context);
                containerSource = VerifyRepository(containerSource, this.RepositoryName);

                var containerId = new ContainerId(this.DockerRepository, containerSource.GetFullRepository((ICredentialResolutionContext)context), this.Tag);

                var escapeArg = GetEscapeArg(context);
                var args = $"build {(this.DockerfileName != "Dockerfile" ? $"--f {this.DockerfileName}" : string.Empty)} --force-rm --progress=plain --tag={escapeArg(containerId.FullName)} {this.AdditionalArguments} {escapeArg(sourcePath)}";
                this.LogDebug("Executing docker " + args);

                var startInfo = new RemoteProcessStartInfo
                {
                    FileName = this.DockerExePath,
                    Arguments = args,
                    WorkingDirectory = sourcePath
                };

                using (var process = procExec.CreateProcess(startInfo))
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

                if (!string.IsNullOrEmpty(this.DockerRepository))
                {
                    await this.PushAsync(context, containerId);

                    if (this.RemoveAfterPush)
                    {
                        this.LogDebug("Removing local image after successful push...");
                        await this.RemoveAsync(context, containerId);
                    }
                }

                if (this.AttachToBuild)
                    await this.AttachToBuildAsync(context, containerId);
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
                    "Build ",
                    new Hilite(config[nameof(DockerRepository)] + ":" + config[nameof(Tag)]),
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
