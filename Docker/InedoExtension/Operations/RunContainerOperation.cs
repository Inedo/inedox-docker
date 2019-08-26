using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Run-Container")]
    [ScriptNamespace("Docker")]
    [Description("Runs a Docker container.")]
    public sealed class RunContainerOperation : DockerOperation
    {
        [Required]
        [ScriptAlias("Repository")]
        [DisplayName("Repository name")]
        public string RepositoryName { get; set; }
        [Required]
        [ScriptAlias("Tag")]
        public string Tag { get; set; }
        [Required]
        [DisplayName("Configuration file name")]
        [ScriptAlias("ConfigFileName")]
        public string ConfigFileName { get; set; }
        [Required]
        [DisplayName("Instance")]
        [ScriptAlias("Instance")]
        public string ConfigInstanceName { get; set; }
        [DisplayName("Container name")]
        [ScriptAlias("Container")]
        [PlaceholderText("auto")]
        public string ContainerName { get; set; }

        [DisplayName("Run in background")]
        [ScriptAlias("RunInBackground")]
        [DefaultValue(true)]
        public bool RunInBackground { get; set; } = true;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var containerConfigArgs = await this.GetContainerConfigText(context);
            if (containerConfigArgs == null)
                return;

            var args = new StringBuilder("run ");
            args.Append(containerConfigArgs);
            args.Append(' ');
            if (this.RunInBackground)
                args.Append("-d ");

            if (!string.IsNullOrWhiteSpace(this.ContainerName))
                args.Append($"--name {this.ContainerName} ");

            args.Append(this.RepositoryName);
            args.Append(':');
            args.Append(this.Tag);

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
                    "Run ",
                    new Hilite(config[nameof(RepositoryName)] + ":" + config[nameof(Tag)]),
                    " Docker image"
                ),
                new RichDescription(
                    "using ",
                    new Hilite(config[nameof(ConfigInstanceName)]),
                    " instance of ",
                    new Hilite(config[nameof(ConfigFileName)]),
                    " configuration file"
                )
            );
        }

        private async Task<string> GetContainerConfigText(IOperationExecutionContext context)
        {
            var deployer = await context.TryGetServiceAsync<IConfigurationFileDeployer>();
            if (deployer == null)
                throw new NotSupportedException("Configuration files are not supported in this context.");

            using (var writer = new StringWriter())
            {
                if (!await deployer.WriteAsync(writer, this.ConfigFileName, this.ConfigInstanceName, this))
                    return null;

                return Regex.Replace(writer.ToString(), @"\r?\n", " ");
            }
        }
    }
}
