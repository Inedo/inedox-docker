using Inedo.Agents;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations.Compose;

[ScriptAlias("Compose-DeployComposeFile")]
[ScriptNamespace("Docker")]
[Description("Deploys a Docker Compose file stored in BuildMaster.")]
public sealed class DockerComposeDeployComposeFileOperation : ExecuteOperation
{
    [DisplayName("Docker Compose file")]
    [ScriptAlias("DockerComposeFile")]
    [DefaultValue("docker-compose.yml")]
    //[SuggestableValue(typeof(ConfigurationFileNameSuggestionProvider))]

    public string? DockerComposeFile { get; set; }

    [DisplayName("Docker Compose file instance")]
    [ScriptAlias("DockerComposeFileInstance")]
    [DefaultValue("$PipelineStageName")]
    //[SuggestableValue(typeof(InstanceNameSuggestionProvider))]
    public string? DockerComposeFileInstance { get; set; }

    [ScriptAlias("To")]
    [DisplayName("To directory")]
    [PlaceholderText("$WorkingDirectory")]
    [Inedo.Web.FieldEditMode(Inedo.Web.FieldEditMode.ServerDirectoryPath)]

    public string? TargetDirectory { get; set; }
    [ScriptAlias("OutputFileName")]
    [DisplayName("To file name")]
    [PlaceholderText("do not rename file")]
    public string? OutputFileName { get; set; }

    public override async Task ExecuteAsync(IOperationExecutionContext context)
    {
        if (!string.IsNullOrEmpty(this.DockerComposeFile) && string.IsNullOrEmpty(this.DockerComposeFileInstance))
            throw new ExecutionFailureException($"An Instance is required when specifying a Docker Compose file.");

        var deployer = (await context.TryGetServiceAsync<IConfigurationFileDeployer>())
                ?? throw new ExecutionFailureException("Configuration files are not supported in this context.");
        var fileOps = await context.TryGetServiceAsync<IFileOperationsExecuter>();

        this.LogDebug("Ensuring deployment path is created");
        var targetPath = string.IsNullOrEmpty(this.TargetDirectory)
                    ? context.WorkingDirectory
                    : context.ResolvePath(this.TargetDirectory);
        await fileOps!.CreateDirectoryAsync(targetPath);

        using var writer = new StringWriter();
        if (!await deployer.WriteAsync(writer, this.DockerComposeFile, this.DockerComposeFileInstance, this))
            throw new ExecutionFailureException("Error reading Docker Compose File.");

        var dockerComposeFilePath = fileOps.CombinePath(targetPath, string.IsNullOrWhiteSpace(this.OutputFileName) ? this.DockerComposeFile! : this.OutputFileName!);

        this.LogInformation($"Deploying the {this.DockerComposeFileInstance} of {this.DockerComposeFile} to {dockerComposeFilePath}");
        fileOps.WriteAllText(dockerComposeFilePath, writer.ToString(), InedoLib.UTF8Encoding);

    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        return new ExtendedRichDescription(
            new RichDescription(
                "Deploy the Docker Compose file ",
                new Hilite(config[nameof(DockerComposeFileInstance)]),
                " instance of ",
                new Hilite(config[nameof(DockerComposeFile)]),
                "."
            )
        );
    }
}
