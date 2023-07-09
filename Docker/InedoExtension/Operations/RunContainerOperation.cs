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
        [SuggestableValue(typeof(RepositoryRresourceSuggestionProvider))]
        [DefaultValue("$DockerRepository")]
        public string? RepositoryResourceName { get; set; }
        [ScriptAlias("Tag")]
        [DefaultValue("$DockerTag")]
        public string? Tag { get; set; }
        [Required]
        [DisplayName("Dockerstart file")]
        [ScriptAlias("DockerstartFile")]
        [ScriptAlias("ConfigFileName", Obsolete = true)]
        [SuggestableValue(typeof(ConfigurationSuggestionProvider))]
        [DefaultValue("Dockerstart")]
        public string? DockerstartFileName { get; set; }
        [DisplayName("Dockerstart instance")]
        [ScriptAlias("DockerstartFileInstance")]
        [ScriptAlias("ConfigFileInstanceName", Obsolete = true)]
        [SuggestableValue(typeof(ConfigurationInstanceSuggestionProvider))]
        [DefaultValue("$PipelineStageName")]
        public string? DockerstartFileInstance { get; set; }

        [Category("Legacy")]
        [ScriptAlias("RepositoryName")]
        [DisplayName("Override repository name")]
        [PlaceholderText("Do not override repository")]
        public string? LegacyRepositoryName { get; set; }

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
        public bool RunInBackground { get; set; } = true;
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
            if (string.IsNullOrEmpty(this.DockerstartFileName))
                throw new ExecutionFailureException($"A DockerstartFileName was not specified.");
            if (string.IsNullOrEmpty(this.DockerstartFileInstance))
                throw new ExecutionFailureException($"A DockerstartFileInstance was not specified.");

            var repoResource = this.CreateRepository(context, this.RepositoryResourceName, this.LegacyRepositoryName);
            var repository = repoResource.GetRepository(context);
            if (string.IsNullOrEmpty(repository))
                throw new ExecutionFailureException($"Docker repository \"{this.RepositoryResourceName}\" has an unexpected name.");

            if (string.IsNullOrEmpty(this.ContainerName))
                this.ContainerName = repository.Split('/').Last();

            var dockerstartText = await getDockerstartTextAsync();

            var repositoryAndTag = $"{repository}:{this.Tag}";

            var client = await DockerClientEx.CreateAsync(this, context);
            await client.LoginAsync(repoResource);
            try
            {
                await client.DockerAsync($"pull {client.EscapeArg(repositoryAndTag)}");

                var runArgs = new StringBuilder($"run {dockerstartText} --rm --name {client.EscapeArg(this.ContainerName)}");
                if (this.RunInBackground)
                    runArgs.Append(" -d");
                if (!string.IsNullOrWhiteSpace(AdditionalArguments))
                    runArgs.Append($" {this.AdditionalArguments} ");
                runArgs.Append($" {client.EscapeArg(repositoryAndTag)}");

                await client.DockerAsync(runArgs.ToString());
            }
            finally
            {
                await client.LogoutAsync();
            }

            async Task<string> getDockerstartTextAsync()
            {
                var deployer = (await context.TryGetServiceAsync<IConfigurationFileDeployer>())
                    ?? throw new ExecutionFailureException("Configuration files are not supported in this context.");

                using var writer = new StringWriter();
                if (!await deployer.WriteAsync(writer, this.DockerstartFileName, this.DockerstartFileInstance, this))
                    throw new ExecutionFailureException("Error reading Dockerstart configuration file.");

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
                    new Hilite(config[nameof(DockerstartFileInstance)]),
                    " instance of ",
                    new Hilite(config[nameof(DockerstartFileName)]),
                    " Dockerstart file"
                )
            );
        }
    }
}
