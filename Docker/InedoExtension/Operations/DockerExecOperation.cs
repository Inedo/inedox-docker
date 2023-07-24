using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;


namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Docker-Exec")]
    [ScriptNamespace("Docker")]
    [DisplayName("Docker exec in Container")]
    [Description("Attaches and runs a command in an already running container")]
    public class DockerExecOperation : DockerOperation
    {
        [DisplayName("Container name")]
        [ScriptAlias("ContainerName")]
        [DefaultValue("default (based on $DockerRepository)")]
        public string ContainerName { get; set; }


        [Required]
        [DisplayName("Command")]
        [ScriptAlias("Command")]
        [PlaceholderText("eg. sh -c \"echo a && echo b\"")]
        public string Command { get; set; }

        [DisplayName("Working directory in container")]
        [ScriptAlias("WorkDir")]
        public string WorkDir { get; set; }

        [DisplayName("Log output (interactive)")]
        [ScriptAlias("Interactive")]
        [Description("Keep STDIN open even if not attached")]
        [DefaultValue(true)]
        public bool? Interactive { get; set; }

        [DisplayName("Run in background (detach)")]
        [ScriptAlias("RunInBackground")]
        [Description("Detached mode: run command in the background")]
        [DefaultValue(false)]
        public bool? RunInBackground { get; set; }

        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Addtional arguments")]
        [Description("Additional arguments for the docker CLI exec command, such as --env key=value")]
        public string AdditionalArguments { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (string.IsNullOrEmpty(this.ContainerName))
            {
                var maybeVariable = context.TryGetVariableValue(new RuntimeVariableName("DockerRepository", RuntimeValueType.Scalar));
                if (maybeVariable == null)
                {
                    var maybeFunc = context.TryGetFunctionValue("DockerRepository");
                    if (maybeFunc == null)
                        throw new ExecutionFailureException($"A ContainerName was not specified and $DockerRepository could not be resolved.");
                    else
                        this.ContainerName = maybeFunc.Value.AsString()!.Split('/').Last();
                }
                else
                    this.ContainerName = maybeVariable.Value.AsString()!.Split('/').Last();
            }

            var escapeArg = GetEscapeArg(context);

            var args = new StringBuilder("exec ");
            if (this.RunInBackground ?? false)
                args.Append("--detach ");
            if (this.Interactive ?? true)
                args.Append("--interactive ");
            if (!string.IsNullOrWhiteSpace(this.WorkDir))
                args.Append($"--workdir {escapeArg(this.WorkDir)} ");

            if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
                args.Append($"{this.AdditionalArguments} ");

            args.Append($"{escapeArg(this.ContainerName)} {this.Command}");


            var argsText = args.ToString();
            this.LogDebug($"Executing docker {argsText}...");

            int result = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = this.DockerExePath,
                    Arguments = argsText
                }
            );

            this.Log(result == 0 ? MessageLevel.Debug : MessageLevel.Error, "Docker exited with code " + result);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Execute ",
                    new Hilite(config[nameof(Command)]),
                    " on running container named ",
                    new Hilite(config[nameof(ContainerName)])

                )
            );
        }
    }
}
