using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inedo.Agents;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.IO;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations;

[ScriptAlias("Build-Image")]
[ScriptNamespace("Docker")]
[Description("Builds a Docker image using a Dockerfile template and pushes it to the specified repository.")]
public sealed partial class BuildImageOperation : DockerOperation
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

    [Undisclosed]
    [Category("Dockerfile template")]
    [ScriptAlias("DockerfileVariables")]
    [ScriptAlias("TemplateArguments")]
    [DisplayName("Addtional template variables values")]
    [FieldEditMode(FieldEditMode.Multiline)]
    [PlaceholderText("eg. %(name: value, ...)")]
    public IDictionary<string, RuntimeValue>? TemplateArguments { get; set; }

    [Undisclosed]
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

    public override OperationProgress? GetProgress()
    {
        int total = this.vertices.Count;
        int completed = 0;

        string? currentName = null;

        foreach (var v in this.vertices.Values)
        {
            if (v.Completed)
                completed++;
            else
                currentName = v.Name;
        }

        return new OperationProgress(total > 0 ? (completed * 100 / total) : null, currentName);
    }

    public override sealed async Task ExecuteAsync(IOperationExecutionContext context)
    {
        if (string.IsNullOrEmpty(this.Tag))
            throw new ExecutionFailureException("A tag was not specified.");

        var repoResource = this.CreateRepository(context, this.RepositoryResourceName, this.LegacyRepositoryName);

        var client = await DockerClient.CreateAsync(this, context);

        var esc = client.EscapeArg;
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
            var item = SDK.GetRaftItems(RaftItemType.BuildFile, context)
                .FirstOrDefault(i => string.Equals(i.Name, this.DockerfileTemplate, StringComparison.CurrentCultureIgnoreCase))
                ?? SDK.GetRaftItems(RaftItemType.TextFile, context)
                .FirstOrDefault(i => string.Equals(i.Name, this.DockerfileTemplate, StringComparison.CurrentCultureIgnoreCase))
                ?? throw new ExecutionFailureException($"Dockerfile template \"{this.DockerfileTemplate}\" not found.");

            this.LogDebug("Applying template...");
            var result = await context.ApplyTextTemplateAsync(item.Content, this.TemplateArguments != null ? new Dictionary<string, RuntimeValue>(this.TemplateArguments) : null);
            await fileOps.WriteAllTextAsync(dockerfilePath, result, InedoLib.UTF8Encoding);
        }

        var repository = repoResource.GetRepository(context);
        if (string.IsNullOrEmpty(repository))
            throw new ExecutionFailureException($"Docker repository \"{this.RepositoryResourceName}\" has an unexpected name.");

        var repositoryAndTag = $"{repository}:{this.Tag}".ToLowerInvariant();

        var buildArgs = new StringBuilder();
        buildArgs.Append(" --progress=rawjson");
        buildArgs.Append($" --tag={esc(repositoryAndTag)}");
        if (PathEx.GetFileName(dockerfilePath) != "Dockerfile")
            buildArgs.Append($" --f {esc(adjustForWsl(dockerfilePath))}");
        if (!string.IsNullOrEmpty(this.AdditionalArguments))
            buildArgs.Append($" {this.AdditionalArguments}");
        buildArgs.Append($" {esc(adjustForWsl(sourcePath))}");

        await client.DockerAsync($"buildx build{buildArgs}", errorReceived: processProgress);

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

        string adjustForWsl(string path)
        {
            if (client.ClientType != DockerClientType.Wsl)
                return path;

            this.LogInformation($"Converting \"{path}\" for use on WSL...");

            // c:\something\somewhere --> /mnt/c/something/somewhere
            return $"/mnt/{path[0]}{path[2..].Replace("\\", "/")}";
        }

        void processProgress(string rawjson)
        {
            var node = JsonSerializer.Deserialize(rawjson, DockerClientJsonContext.Default.RootLogNode);
            if (node is null)
                return;

            if (node.Vertexes is not null)
            {
                foreach (var v in node.Vertexes)
                {
                    if (!vertices.TryGetValue(v.Digest, out var vertex))
                    {
                        vertex = new ActiveVertex(v.Name, context.Log.CreateNestedLog(GetVertexName(v.Name)));
                        vertices[v.Digest] = vertex;
                    }
                    else
                    {
                        if (v.Completed.HasValue && !vertex.Completed)
                            vertex.Completed = true;
                    }
                }
            }

            if (node.Statuses is not null)
            {
                foreach (var s in node.Statuses)
                {
                    if (this.vertices.TryGetValue(s.Vertex, out var vertex))
                    {
                        vertex.StatusId ??= s.Id;
                        vertex.Current = s.Current;
                    }
                }
            }

            if (node.Logs is not null)
            {
                foreach (var l in node.Logs)
                {
                    if (this.vertices.TryGetValue(l.Vertex, out var vertex))
                    {
                        vertex.Log.Log(
                            l.Stream == 2 ? MessageLevel.Debug : MessageLevel.Debug,
                            Encoding.UTF8.GetString(l.Data).Trim()
                        );
                    }
                }
            }
        }
    }

    private static string GetVertexName(string rawName)
    {
        if (rawName.Length > 50)
            return rawName[..50];
        else
            return rawName;
    }

    private readonly ConcurrentDictionary<string, ActiveVertex> vertices = [];

    private sealed class ActiveVertex(string name, IScopedLog log)
    {
        public string Name { get; } = name;
        public IScopedLog Log { get; } = log;
        public string StatusId { get; set; } = string.Empty;
        public long Current { get; set; }
        public bool Completed { get; set; }
    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Build ",
                new Hilite($"{config[nameof(RepositoryResourceName)]}:{config[nameof(Tag)]}"),
                " Docker image"
            ),
            new RichDescription(
                "from ",
                new DirectoryHilite(config[nameof(SourceDirectory)])
            )
        );
    }

    [GeneratedRegex(@"\A[0-9a-f]+:\s*Waiting")]
    private static partial Regex WaitingRegex();
}
