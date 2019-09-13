using Inedo.Extensibility;
using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.Docker.VariableFunctions
{
    [ScriptAlias("DockerExePath")]
    [ExtensionConfigurationVariable(Type = ExpectedValueDataType.String)]
    public sealed class DockerExePathVariableFunction : ScalarVariableFunction
    {
        protected override object EvaluateScalar(IVariableFunctionContext context) => "docker";
    }
}
