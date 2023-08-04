using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Docker;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Extensions.SecureResources;
using Inedo.IO;
using Inedo.Web;

#nullable enable

namespace Inedo.Extensions.Docker.Operations
{


    [ScriptAlias("Build-Image")]
    [ScriptNamespace("Docker")]
    [DisplayName("Build Docker Image")]
    [Description("Builds a Docker image using a Dockerfile template and pushes it to the specified repository.")]
    public sealed partial class BuildImageOperation : DockerOperation_ForTheNew
    {
        [ScriptAlias("From")]
        [DisplayName("From")]
        [PlaceholderText("$WorkingDirectory")]
        [FieldEditMode(FieldEditMode.ServerDirectoryPath)]
        public string? SourceDirectory { get; set; }
        [ScriptAlias("Repository")]
        [ScriptAlias("Source")]
        [DisplayName("Repository")]
        [SuggestableValue(typeof(RepositoryResourceSuggestionProvider))]
        [DefaultValue("$DockerRepository")]
        public string? RepositoryResourceName { get; set; }
        [ScriptAlias("Tag")]
        [DefaultValue("$ReleaseNumber-pre.$BuildNumber")]
        public string? Tag { get; set; }

        [Category("Dockerfile (template)")]
        [ScriptAlias("Dockerfile")]
        [ScriptAlias("DockerfileAsset")]
        [DisplayName("Dockerfile template")]
        [SuggestableValue(typeof(DockerfileSuggestionProvider))]
        public string? DockerfileTemplate { get; set; }
        [Category("Dockerfile template")]
        [ScriptAlias("DockerfileVariables")]
        [ScriptAlias("TemplateArguments")]
        [DisplayName("Addtional template variables values")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [PlaceholderText("eg. %(name: value, ...)")]
        public IDictionary<string, RuntimeValue>? TemplateArguments { get; set; }

        [Category("Legacy")]
        [ScriptAlias("RepositoryName")]
        [DisplayName("Override repository name")]
        [PlaceholderText("Do not override repository")]
        public string? LegacyRepositoryName { get; set; }

        [Category("Advanced")]
        [ScriptAlias("DockerfileName")]
        [DisplayName("Dockerfile name")]
        [PlaceholderText("Dockerfile")]
        [DefaultValue("Dockerfile")]
        [Description("The name of the Dockerfile to use when building your image; ignored when a Dockerfile template is used.")]
        public string? DockerfileName { get; set; }
        [Category("Advanced")]
        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Addtional arguments")]
        [Description("Additional arguments for the docker CLI build command, such as --build-arg=ARG_NAME=value")]
        public string? AdditionalArguments { get; set; }
        [Category("Advanced")]
        [ScriptAlias("AttachToBuild")]
        [DisplayName("Attach to build")]
        [DefaultValue(true)]
        public bool AttachToBuild { get; set; } = true;
        [Category("Advanced")]
        [ScriptAlias("RemoveAfterPush")]
        [DisplayName("Remove after pushing")]
        public bool RemoveAfterPush { get; set; }

        public override sealed async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (string.IsNullOrEmpty(this.Tag))
                throw new ExecutionFailureException($"A Tag was not specified.");
            var repoResource = this.CreateRepository(context, this.RepositoryResourceName, this.LegacyRepositoryName);

            var client = await DockerClientEx.CreateAsync(this, context);
            await client.LoginAsync(repoResource);

            try
            {
                var esc = client.EscapeArg;
                string cvt(string path)
                {
                    if (client.ClientType != DockerClientType.Wsl)
                        return path;
                    this.LogInformation($"Converting \"{path}\" for use on WSL...");
                    
                    // c:\something\somewhere --> /mnt/c/something/somewhere
                    return "/mnt/" + path[0] + path.Substring(2).Replace("\\", "/");

                };
                var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();

                await fileOps.CreateDirectoryAsync(context.WorkingDirectory);
                
                var sourcePath = string.IsNullOrEmpty(this.SourceDirectory)
                    ? context.WorkingDirectory
                    : context.ResolvePath(this.SourceDirectory);
                await fileOps.CreateDirectoryAsync(sourcePath);

                string dockerfilePath;
                if (string.IsNullOrWhiteSpace(this.DockerfileTemplate))
                {
                    if (string.IsNullOrEmpty(this.DockerfileName))
                        throw new ExecutionFailureException("DockerfileName must be specified when Dockerfile is empty.");

                    dockerfilePath = fileOps.CombinePath(sourcePath, this.DockerfileName);
                }
                else
                {
                    dockerfilePath = fileOps.CombinePath(sourcePath, "Dockerfile");

                    this.LogDebug($"Loading Dockerfile template \"{this.DockerfileTemplate}\"...");
                    var item = SDK
                        .GetRaftItems(RaftItemType24.BuildFile, context)
                        .FirstOrDefault(i => string.Equals(i.Name, this.DockerfileTemplate, System.StringComparison.CurrentCultureIgnoreCase))
                        ?? SDK
                        .GetRaftItems(RaftItemType.TextTemplate, context)
                        .FirstOrDefault(i => string.Equals(i.Name, this.DockerfileTemplate, System.StringComparison.CurrentCultureIgnoreCase))
                        ?? throw new ExecutionFailureException($"Dockerfile template \"{this.DockerfileTemplate}\" not found.");

                    this.LogDebug($"Applying template...");
                    var _ = await context.ApplyTextTemplateAsync(item.Content, this.TemplateArguments != null ? new Dictionary<string, RuntimeValue>(this.TemplateArguments) : null);
                    await fileOps.WriteAllTextAsync(dockerfilePath, _, InedoLib.UTF8Encoding);
                }

                var repository = repoResource.GetRepository(context);
                if (string.IsNullOrEmpty(repository))
                    throw new ExecutionFailureException($"Docker repository \"{this.RepositoryResourceName}\" has an unexpected name.");

                var repositoryAndTag = $"{repository}:{this.Tag}";

                var buildArgs = new StringBuilder();
                {
                    buildArgs.Append($" --force-rm --progress=plain");
                    buildArgs.Append($" --tag={esc(repositoryAndTag)}");
                    if (PathEx.GetFileName(dockerfilePath) != "Dockerfile")
                        buildArgs.Append($" --f {esc(cvt(dockerfilePath))}");
                    if (!string.IsNullOrEmpty(this.AdditionalArguments))
                        buildArgs.Append($" {this.AdditionalArguments}");
                    buildArgs.Append($" {esc(cvt(sourcePath))}");
                }
                await client.DockerAsync("build" + buildArgs.ToString(), true);

                this.LogInformation("Docker build successful.");

                await client.DockerAsync($"push {esc(repositoryAndTag)}");

                if (this.AttachToBuild)
                {
                    var digest = await client.GetDigestAsync(repositoryAndTag);
                    var containerManager = await context.TryGetServiceAsync<IContainerManager>()
                        ?? throw new ExecutionFailureException("Unable to get service IContainerManager to attach to build.");
                    await containerManager.AttachContainerToBuildAsync(new(repository, this.Tag, digest, this.RepositoryResourceName), context.CancellationToken);
                }

                if (this.RemoveAfterPush)
                    await client.DockerAsync($"rmi {esc(repositoryAndTag)}");
            }
            finally
            {
                await client.LogoutAsync();
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Build ",
                    new Hilite(config[nameof(RepositoryResourceName)] + ":" + config[nameof(Tag)]),
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
