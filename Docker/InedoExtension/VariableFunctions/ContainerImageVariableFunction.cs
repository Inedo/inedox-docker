using System.ComponentModel;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensibility.VariableFunctions;
using Inedo.Extensions.SecureResources;
using static Inedo.Extensions.Docker.Operations.DockerOperation;

namespace Inedo.Extensions.Docker.VariableFunctions
{
    [Undisclosed]
    [ScriptAlias("ContainerImage")]
    [Description("")]
    [ExtensionConfigurationVariable(Type = ExpectedValueDataType.String)]
    [Tag("Docker")]
    [Example(@"$ContainerImage(ContainerSource, $ContainerRepositoryName, $ImageTag)")]
    [Category("Docker")]
    public sealed class ContainerImageVariableFunction : ScalarVariableFunction
    {
        [DisplayName("Repository")]
        [VariableFunctionParameter(0)]
        [Description("Container Registry Secured Resource Name")]
        public string DockerRepository { get; set; }
        [DisplayName("RepositoryName")]
        [VariableFunctionParameter(1)]
        [Description("Container Repository name")]
        public string RepositoryName { get; set; }
        [DisplayName("Tag")]
        [VariableFunctionParameter(2)]
        [Description("eg. $ReleaseNumber-ci.$BuildNumber")]
        public string Tag { get; set; }

        protected override object EvaluateScalar(IVariableFunctionContext context) => this.AssembleImageName(context);

        private string AssembleImageName(IVariableFunctionContext context)
        {
            var containerSource = DockerRepository24.Create(this.DockerRepository, (IResourceResolutionContext)context);
            containerSource = this.VerifyRepository(containerSource, this.RepositoryName);
            var containerId = new ContainerId(this.DockerRepository, containerSource.GetRepository((ICredentialResolutionContext)context), this.Tag);
            return containerId.FullName;
        }
        private DockerRepository24 VerifyRepository(DockerRepository24 containerSource, string repositoryName)
        {
            if (containerSource.IsContainerSource(out var genericDockerRepository))
            {
                if (string.IsNullOrWhiteSpace(genericDockerRepository.Repository))
                {
                    if (!string.IsNullOrWhiteSpace(genericDockerRepository.LegacyRegistryPrefix))
                    {
                        if (!string.IsNullOrWhiteSpace(repositoryName))
                        {
                            Logger.Log(MessageLevel.Warning, "The RepositoryName parameter is deprecated; instead, edit ${Repository} to include the repository name.");
                            genericDockerRepository.Repository = $"{genericDockerRepository.LegacyRegistryPrefix.TrimEnd('/')}/{repositoryName}";
                        }
                        else
                        {
                            Logger.Log(MessageLevel.Error, "Repository name is required for generic docker repositories.");
                            return null;
                        }
                    }
                    else
                    {
                        Logger.Log(MessageLevel.Error, "Repository name is required for generic docker repositories.");
                        return null;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(repositoryName))
            {
                Logger.Log(MessageLevel.Warning, "The RepositoryName parameter is deprecated; instead, edit ${Repository} to include the repository name.");
            }

            return containerSource;
        }
    }
}
