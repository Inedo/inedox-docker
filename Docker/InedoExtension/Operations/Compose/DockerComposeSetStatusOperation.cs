using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations.Compose
{
    [Description("Starts, restarts, stops, pauses, or resumes existing containers for a service.")]
    [ScriptAlias("Compose-SetStatus")]
    public sealed class DockerComposeSetStatusOperation : ComposeServicesOperationBase
    {
        protected override string Command => AH.Switch<ContainerAction, string>(this.Action)
            .Case(ContainerAction.Start, "start")
            .Case(ContainerAction.Restart, "restart")
            .Case(ContainerAction.Stop, "stop")
            .Case(ContainerAction.ForceStop, "kill")
            .Case(ContainerAction.Pause, "pause")
            .Case(ContainerAction.Resume, "resume")
            .End();

        public enum ContainerAction
        {
            Start,
            Restart,
            Stop,
            ForceStop,
            Pause,
            Resume
        }

        [Required]
        [ScriptAlias("Action")]
        public ContainerAction Action { get; set; }

        [Category("Advanced")]
        [ScriptAlias("Timeout")]
        [Description("Used for the Restart and Stop actions.")]
        [DefaultValue(10)]
        public int Timeout { get; set; } = 10;

        [Category("Advanced")]
        [ScriptAlias("Signal")]
        [Description("Used for the ForceStop action.")]
        [DefaultValue("SIGKILL")]
        [SuggestableValue("SIGKILL", "SIGTERM", "SIGINT", "SIGHUP", "SIGQUIT")]
        public string Signal { get; set; } = "SIGKILL";

        public override Task ExecuteAsync(IOperationExecutionContext context)
        {
            return this.RunDockerComposeAsync(context,
                this.Action == ContainerAction.Restart || this.Action == ContainerAction.Stop ? "--timeout" : null,
                this.Action == ContainerAction.Restart || this.Action == ContainerAction.Stop ? this.Timeout.ToString() : null,
                this.Action == ContainerAction.ForceStop ? "-s" : null,
                this.Action == ContainerAction.ForceStop ? this.Signal : null
            );
        }

        protected override string PrepareDescription(IOperationConfiguration config, RichDescription details)
        {
            if (Enum.TryParse<ContainerAction>(config[nameof(Action)], out var action))
            {
                switch (action)
                {
                    case ContainerAction.Stop:
                    case ContainerAction.Restart:
                        details.AppendContent("with a time limit of ", new Hilite(AH.CoalesceString(config[nameof(Timeout)], "10")), " seconds");
                        break;
                    case ContainerAction.ForceStop:
                        details.AppendContent("by sending the ", new Hilite(AH.CoalesceString(config[nameof(Signal)], "SIGKILL")), " signal");
                        break;
                    default:
                        break;
                }
            }

            return config[nameof(Action)];
        }
    }
}
