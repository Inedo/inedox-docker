using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.Docker.VariableFunctions
{
    [ExtensionConfigurationVariable]
    public sealed class DockerExePathVariableFunction : ScalarVariableFunction
    {
        protected override object EvaluateScalar(IVariableFunctionContext context) => "docker";
    }
}
