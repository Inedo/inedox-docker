using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.SecureResources;

namespace Inedo.Extensions.Docker.Operations;

public abstract class DockerOperation : ExecuteOperation
{
    protected DockerOperation()
    {
    }

    [Category("Advanced")]
    [DisplayName("Docker client path")]
    [ScriptAlias("DockerExePath")]
    [DefaultValue("$DockerExePath")]
    public string? DockerExePath { get; set; }

    [Category("Advanced")]
    [DisplayName("Use Docker (WSL)")]
    [PlaceholderText("default (use Docker for Windows)")]
    [Description("When Docker for Windows and Docker on WSL are installed on the same server, Docker for Windows is preferred. Setting this will force Docker (WSL)")]
    public bool UseWsl { get; set; }

    private protected DockerRepository CreateRepository(ICredentialResolutionContext context, string? repositoryResourceName, string? repositoryNameOverride)
    {
        if (string.IsNullOrEmpty(repositoryResourceName))
            throw new ExecutionFailureException("A Docker repository was not specified.");

        var repository = SecureResource.Create(SecureResourceType.DockerRepository, repositoryResourceName, context) as DockerRepository
             ?? throw new ExecutionFailureException($"A Docker repository named \"{repositoryResourceName}\" was not found.");

        if (repository is GenericDockerRepository genericDockerRepository)
        {
            if (string.IsNullOrWhiteSpace(genericDockerRepository.Repository))
            {
                if (string.IsNullOrWhiteSpace(genericDockerRepository.LegacyRegistryPrefix))
                    throw new ExecutionFailureException("LegacyRegistryPrefix is required for generic docker repositories when Repository is not specified.");

                if (string.IsNullOrWhiteSpace(repositoryNameOverride))
                    throw new ExecutionFailureException("When LegacyRegistryPrefix is used, a RepositoryName override must also be specified.");

                this.LogWarning($"The RepositoryName override parameter is deprecated; instead, edit \"{repositoryResourceName}\" to include the repository name.");
                genericDockerRepository.Repository = $"{genericDockerRepository.LegacyRegistryPrefix.TrimEnd('/')}/{repositoryNameOverride}";
                this.LogDebug($"Repository is \"{genericDockerRepository.Repository}\".");
            }
        }
        else if (!string.IsNullOrWhiteSpace(repositoryNameOverride))
        {
            this.LogWarning($"Specifying the Repository using the RepositoryName parameter is deprecated; instead, edit \"{repositoryResourceName}\" to include the repository name.");
        }

        return repository;
    }
}
