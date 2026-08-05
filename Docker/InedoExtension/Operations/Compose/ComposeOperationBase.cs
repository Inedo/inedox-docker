using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.Docker.Operations.Compose
{
    [DefaultProperty(nameof(ProjectName))]
    [Tag("docker-compose")]
    public abstract class ComposeOperationBase : DockerOperation_ForTheNew
    {
        protected virtual string Command => null;

        [ScriptAlias("ComposeFile")]
        [DisplayName("Compose file path")]
        [DefaultValue("docker-compose.yml")]
        public string ComposeFile { get; set; }

        [ScriptAlias("EnvFile")]
        [Description(".env File")]
        public string EnvFile { get; set; }

        [ScriptAlias("WorkingDirectory")]
        [DisplayName("Working directory")]
        [PlaceholderText("$WorkingDirectory")]
        public string WorkingDirectory { get; set; }


        [Category("Advanced")]
        [ScriptAlias("ProjectName")]
        [DisplayName("Project name")]
        public string ProjectName { get; set; }


        [Category("Advanced")]
        [ScriptAlias("Profile")]
        [DisplayName("Profile")]
        public string Profile { get; set; }


        [Category("Advanced")]
        [ScriptAlias("AddArgs")]
        [DisplayName("Additional docker compose arguments")]
        [FieldEditMode(FieldEditMode.Multiline)]
        public virtual IEnumerable<string> AddArgs { get; set; }

        [Category("Advanced")]
        [DefaultValue(false)]
        [ScriptAlias("Verbose")]
        public bool Verbose { get; set; }

        protected async virtual Task RunDockerComposeAsync(IOperationExecutionContext context, params string[] args)
        {
            var fileOps = await context.Agent.TryGetServiceAsync<ILinuxFileOperationsExecuter>() ?? await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var workingDirectory = context.ResolvePath(string.IsNullOrWhiteSpace(this.WorkingDirectory) ? context.WorkingDirectory : this.WorkingDirectory);
            
            this.LogDebug($"Working directory: {workingDirectory}");
            await fileOps.CreateDirectoryAsync(workingDirectory);

            var client = await DockerClientEx.CreateAsync(this, context);

            var configText = this.CreateExecutionParamenters(client.EscapeArg, args);

            await client.DockerAsync(configText, true);
        }

        protected virtual string CreateExecutionParamenters(Func<string, string> escapeFunc, params string[] args)
        {
            var configText = new StringBuilder("compose ");

            if (!string.IsNullOrWhiteSpace(this.ProjectName))
                configText.Append($"--project-name {this.ProjectName} ");

            if (!string.IsNullOrWhiteSpace(this.Profile))
                configText.Append($"--profile {this.Profile} ");

            if (!string.IsNullOrWhiteSpace(this.EnvFile))
                configText.Append($"--env-file {escapeFunc(this.EnvFile)} ");

            if (!string.IsNullOrWhiteSpace(this.ComposeFile))
                configText.Append($"--file {this.ComposeFile}");

            if (this.Verbose)
                configText.Append("--verbose ");

            configText.Append($"--no-ansi ");
            configText.Append($"{string.Join(' ', (this.AddArgs ?? []).Concat(args ?? []).Where(arg => arg != null))} ");

            configText.Append($"{this.Command} ");

            return configText.ToString();
        }

        protected override void LogProcessError(string text) =>this.LogDebug(text);
    }
}
