using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

#nullable enable

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Run-Container")]
    [ScriptNamespace("Docker")]
    [DisplayName("Run Docker Container")]
    [Description("Runs a Docker container on a container host server using a container configuration file.")]
    public sealed class RunContainerOperation : DockerOperation
    {
        [ScriptAlias("Repository")]
        [ScriptAlias("Source", Obsolete = true)]
        [DisplayName("Repository")]
        [SuggestableValue(typeof(RepositoryResourceSuggestionProvider))]
        [DefaultValue("$DockerRepository")]
        public string? RepositoryResourceName { get; set; }
        [ScriptAlias("Tag")]
        [DefaultValue("$DockerTag")]
        public string? Tag { get; set; }

        [Category("Run Config")]
        [DisplayName("Docker Run Config")]
        [ScriptAlias("DockerRunConfig")]
        [ScriptAlias("ConfigFileName", Obsolete = true)]
        [SuggestableValue(typeof(ConfigurationSuggestionProvider))]
        [DefaultValue("DockerRun")]
        public string? DockerRunConfig { get; set; }
        [Category("Run Config")]
        [DisplayName("Docker Run Config instance")]
        [ScriptAlias("DockerRunConfigInstance")]
        [ScriptAlias("ConfigFileInstanceName", Obsolete = true)]
        [SuggestableValue(typeof(ConfigurationInstanceSuggestionProvider))]
        [DefaultValue("$PipelineStageName")]
        public string? DockerRunConfigInstance { get; set; }

        [Category("Legacy")]
        [ScriptAlias("RepositoryName")]
        [DisplayName("Override repository name")]
        [PlaceholderText("Do not override repository")]
        public string? LegacyRepositoryName { get; set; }

        [Category("Advanced")]
        [DisplayName("Restart policy")]
        [ScriptAlias("Restart")]
        [DefaultValue("unless-stopped")]
        [SuggestableValue("no", "on-failure", "always", "unless-stopped")]
        public string? RestartPolicy { get; set; }
        [Category("Advanced")]
        [DisplayName("Container name")]
        [ScriptAlias("ContainerName")]
        [ScriptAlias("Container", Obsolete = true)]
        [PlaceholderText("use repository name")]
        public string? ContainerName { get; set; }
        [Category("Advanced")]
        [DisplayName("Run in background")]
        [ScriptAlias("RunInBackground")]
        [DefaultValue(true)]
        public bool RunInBackground { get; set; }
        [Category("Advanced")]
        [DisplayName("Remove the container on exit")]
        [ScriptAlias("RemoveOnExit")]
        public bool RemoveOnExit { get; set; }
        [Category("Advanced")]
        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Addtional arguments")]
        [Description("Additional arguments for the docker CLI exec command, such as --env key=value")]
        public string? AdditionalArguments { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (string.IsNullOrEmpty(this.RepositoryResourceName))
                throw new ExecutionFailureException($"A RepositoryResourceName was not specified.");
            if (string.IsNullOrEmpty(this.Tag))
                throw new ExecutionFailureException($"A Tag was not specified.");
            if (!string.IsNullOrEmpty(this.DockerRunConfig) && string.IsNullOrEmpty(this.DockerRunConfigInstance))
                throw new ExecutionFailureException($"An Instance is required when specifying a Docker Run Config.");

            var repoResource = this.CreateRepository(context, this.RepositoryResourceName, this.LegacyRepositoryName);
            var repository = repoResource.GetRepository(context);
            if (string.IsNullOrEmpty(repository))
                throw new ExecutionFailureException($"Docker repository \"{this.RepositoryResourceName}\" has an unexpected name.");

            if (string.IsNullOrEmpty(this.ContainerName))
                this.ContainerName = repository.Split('/').Last();

            var dockerRunText = await getDockerRunTextAsync();

            var repositoryAndTag = $"{repository}:{this.Tag}";

            var client = await DockerClientEx.CreateAsync(this, context);
            await client.LoginAsync(repoResource);
            try
            {
                await client.DockerAsync($"pull {client.EscapeArg(repositoryAndTag)}");

                var runArgs = new StringBuilder($"run --name {client.EscapeArg(this.ContainerName)}");
                if (!string.IsNullOrEmpty(dockerRunText))
                    runArgs.Append($" {dockerRunText}");
                if (this.RemoveOnExit)
                    runArgs.Append(" --rm");
                if (this.RunInBackground)
                    runArgs.Append(" -d");
                if (!string.IsNullOrWhiteSpace(AdditionalArguments))
                    runArgs.Append($" {this.AdditionalArguments}");
                runArgs.Append($" {client.EscapeArg(repositoryAndTag)}");

                await client.DockerAsync(runArgs.ToString());
            }
            finally
            {
                await client.LogoutAsync();
            }

            async Task<string?> getDockerRunTextAsync()
            {
                if (string.IsNullOrEmpty(this.DockerRunConfig))
                    return null;

                var deployer = (await context.TryGetServiceAsync<IConfigurationFileDeployer>())
                    ?? throw new ExecutionFailureException("Configuration files are not supported in this context.");

                using var writer = new StringWriter();
                if (!await deployer.WriteAsync(writer, this.DockerRunConfig, this.DockerRunConfigInstance, this))
                    throw new ExecutionFailureException("Error reading Docker Run Config.");

                return Regex.Replace(writer.ToString(), @"\r?\n", " ");
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Run ",
                    new Hilite(config[nameof(RepositoryResourceName)] + ":" + config[nameof(Tag)]),
                    " Docker image"
                ),

                new RichDescription(
                    "using ",
                    new Hilite(config[nameof(DockerRunConfigInstance)]),
                    " instance of ",
                    new Hilite(config[nameof(DockerRunConfig)]),
                    "."
                )
            );
        }
    }
}
