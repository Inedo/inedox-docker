using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensibility.VariableFunctions;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Extensions.SecureResources;
using Inedo.Web;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Inedo.Extensions.Docker.Operations.DockerOperation;

namespace Inedo.Extensions.Docker.VariableFunctions
{
    [ScriptAlias("ContainerImage")]
    [Description("")]
    [ExtensionConfigurationVariable(Type = ExpectedValueDataType.String)]
    [Tag("Docker")]
    [Example(@"$ContainerImage(ContainerSource, $ContainerRepositoryName, $ImageTag)")]
    [Category("Docker")]
    public sealed class ContainerImageVariableFunction : ScalarVariableFunction
    {
        [DisplayName("Source")]
        [VariableFunctionParameter(0)]
        [Description("Container Registry Secured Resource Name")]
        public string ContainerSource { get; set; }
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
            var containerSource = (ContainerSource)SecureResource.Create(this.ContainerSource, (IResourceResolutionContext)context);
            var containerId = new ContainerId(this.ContainerSource, containerSource?.RegistryPrefix, this.RepositoryName, this.Tag);
            return containerId.FullName;
        }
    }
}
