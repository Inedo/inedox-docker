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
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Extensions.SecureResources;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Run-Container")]
    [ScriptNamespace("Docker")]
    [DisplayName("Run Docker Container")]
    [Description("Runs a Docker container on a container host server using a container configuration file.")]
    public sealed class RunContainerOperation : DockerOperation
    {
        [ScriptAlias("Repository")]
        [Category("Source")]
        [ScriptAlias("Source")]
        [DisplayName("Repository")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        [DefaultValue("$DockerRepository")]
        public string DockerRepository { get; set; }

        [ScriptAlias("RepositoryName")]
        [DisplayName("Repository name")]
        [Category("Legacy")]
        public string RepositoryName { get; set; }

        [Required]
        [ScriptAlias("Tag")]
        public string Tag { get; set; }

        [Required]
        [DisplayName("Container Configuration")]
        [ScriptAlias("ConfigFileName")]
        [SuggestableValue(typeof(ConfigurationSuggestionProvider))]
        public string ConfigFileName { get; set; }

        [DisplayName("Container Runtime")]
        [ScriptAlias("ConfigFileInstanceName")]
        [SuggestableValue(typeof(ConfigurationInstanceSuggestionProvider))]
        public string ConfigInstanceName { get; set; }

        [DisplayName("Container name")]
        [ScriptAlias("ContainerName")]
        [ScriptAlias("Container")]
        [PlaceholderText("auto")]
        public string ContainerName { get; set; }

        [DisplayName("Run in background")]
        [ScriptAlias("RunInBackground")]
        [DefaultValue(true)]
        public bool RunInBackground { get; set; } = true;

        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Addtional arguments")]
        [Description("Additional arguments for the docker CLI exec command, such as --env key=value")]
        public string AdditionalArguments { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            await this.LoginAsync(context, this.DockerRepository);
            try
            {
                var containerSource = (DockerRepository)SecureResource.Create(this.DockerRepository, (IResourceResolutionContext)context);
                containerSource = VerifyRepository(containerSource, this.RepositoryName);
                var containerId = new ContainerId(this.DockerRepository, containerSource.GetRepository((ICredentialResolutionContext)context), this.Tag);
                if (!string.IsNullOrEmpty(this.DockerRepository))
                    containerId = await this.PullAsync(context, containerId);

                var containerConfigArgs = await this.GetContainerConfigText(context);

                var escapeArg = GetEscapeArg(context);

                var args = new StringBuilder("run ");
                if (containerConfigArgs != null)
                {
                    args.Append(containerConfigArgs);
                    args.Append(' ');
                }
                if (this.RunInBackground)
                    args.Append("-d ");
                else if (string.IsNullOrWhiteSpace(this.ContainerName))
                    args.Append("--rm ");

                if (!string.IsNullOrWhiteSpace(this.ContainerName))
                    args.Append($"--name {escapeArg(this.ContainerName)} ");

                if (!string.IsNullOrWhiteSpace(AdditionalArguments))
                    args.Append($"{this.AdditionalArguments} ");

                args.Append(escapeArg(containerId.FullName));


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
            finally
            {
                await this.LogoutAsync(context, this.DockerRepository);
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Run ",
                    new Hilite(config[nameof(DockerRepository)] + ":" + config[nameof(Tag)]),
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
            if (string.IsNullOrWhiteSpace(this.ConfigFileName))
                return null;

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
