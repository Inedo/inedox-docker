using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Inedo.Agents;
using Inedo.Extensibility.Operations;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations.Compose;

[Tag("docker-compose")]
public abstract class ComposeOperationBase : DockerOperation
{

    protected ComposeOperationBase()
    {
    }

    [ScriptAlias("ComposeFile")]
    [DisplayName("Compose file path")]
    [DefaultValue("compose.yaml")]
    public string? ComposeFile { get; set; }

    [ScriptAlias("EnvFile")]
    [Description(".env File")]
    public string? EnvFile { get; set; }

    [ScriptAlias("WorkingDirectory")]
    [DisplayName("Working directory")]
    [PlaceholderText("$WorkingDirectory")]
    public string? WorkingDirectory { get; set; }

    [Category("Advanced")]
    [ScriptAlias("ProjectName")]
    [DisplayName("Project name")]
    public string? ProjectName { get; set; }

    [Category("Advanced")]
    [ScriptAlias("Profile")]
    [DisplayName("Profile")]
    public string? Profile { get; set; }

    [Category("Advanced")]
    [ScriptAlias("AddArgs")]
    [DisplayName("Additional docker compose arguments")]
    [FieldEditMode(FieldEditMode.Multiline)]
    public virtual IEnumerable<string>? AddArgs { get; set; }

    [Category("Advanced")]
    [DefaultValue(false)]
    [ScriptAlias("Verbose")]
    public bool Verbose { get; set; }

    protected abstract string Command { get; }

    protected async virtual Task RunDockerComposeAsync(IOperationExecutionContext context, params string?[] args)
    {
        var fileOps = await context.Agent.TryGetServiceAsync<ILinuxFileOperationsExecuter>() ?? await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
        var workingDirectory = context.ResolvePath(string.IsNullOrWhiteSpace(this.WorkingDirectory) ? context.WorkingDirectory : this.WorkingDirectory);

        this.LogDebug($"Working directory: {workingDirectory}");
        await fileOps.CreateDirectoryAsync(workingDirectory);

        var client = await DockerClient.CreateAsync(this, context);

        var configText = this.CreateExecutionParamenters(client.EscapeArg, args);

        this.LogDebug($"Executing docker compose {this.Command}");
        await client.DockerAsync(configText, workingDirectory: workingDirectory, errorReceived: processProgress);

        void processProgress(string rawjson)
        {
            // Improve this logging at some point
            var node = JsonSerializer.Deserialize(rawjson, DockerComposeClientJsonContext.Default.ComposeLogNode);
            if (node is null)
                return;
            this.LogDebug($"{node.Id}{(string.IsNullOrWhiteSpace(node.Parent_id) ? string.Empty : "(" + node.Parent_id + ")")}: {node.Text} {(node.Percent.HasValue ? node.Percent + "%" : string.Empty)}");
        }
    }

    protected virtual string CreateExecutionParamenters(Func<string, string> escapeFunc, params string?[] args)
    {
        var configText = new StringBuilder("compose ");

        if (!string.IsNullOrWhiteSpace(this.ProjectName))
            configText.Append($"--project-name {this.ProjectName} ");

        if (!string.IsNullOrWhiteSpace(this.Profile))
            configText.Append($"--profile {this.Profile} ");

        if (!string.IsNullOrWhiteSpace(this.EnvFile))
            configText.Append($"--env-file {escapeFunc(this.EnvFile)} ");

        if (!string.IsNullOrWhiteSpace(this.ComposeFile))
            configText.Append($"--file {this.ComposeFile} ");

        configText.Append($"--ansi never  --progress=json ");

        configText.Append($"{this.Command} ");

        if (this.Verbose)
            configText.Append("--verbose ");

        configText.Append($"{string.Join(' ', (this.AddArgs ?? []).Concat(args ?? []).Where(arg => arg != null))} ");


        return configText.ToString();
    }

    protected override void LogProcessError(string text) => this.LogDebug(text);

}
