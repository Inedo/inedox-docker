using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Docker.Operations
{
    [ScriptAlias("Push-Image")]
    [ScriptNamespace("Docker")]
    [Description("Pushes a Docker image.")]
    public sealed class PushImageOperation : DockerOperation
    {
        [Required]
        [ScriptAlias("Repository")]
        [DisplayName("Repository name")]
        public string RepositoryName { get; set; }
        [Required]
        [ScriptAlias("Tag")]
        public string Tag { get; set; }
        [Required]
        [ScriptAlias("To")]
        [DisplayName("To")]
        public string ContainerSource { get; set; }
        [ScriptAlias("AttachToBuild")]
        [DisplayName("Attach to build")]
        [DefaultValue(true)]
        public bool AttachToBuild { get; set; } = true;
        [ScriptAlias("DockerLogin")]
        [DisplayName("Log in using source credentials")]
        [DefaultValue(true)]
        public bool UseDockerLogin { get; set; } = true;
        [ScriptAlias("DockerLogout")]
        [DisplayName("Log out after push")]
        [DefaultValue(true)]
        public bool UseDockerLogout { get; set; } = true;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(this.ContainerSource))
            {
                this.LogError("Missing required value \"To\". Please specify the name of a container source.");
                return;
            }

            var source = SDK.GetContainerSources()
                .FirstOrDefault(s => string.Equals(s.Name, this.ContainerSource, StringComparison.OrdinalIgnoreCase));

            if (source == null)
            {
                this.LogError($"Container source \"{this.ContainerSource}\" not found.");
                return;
            }

            bool logout = this.UseDockerLogin && await this.DockerLoginAsync(context, source) && this.UseDockerLogout;

            var rootUrl = GetServerName(source.RegistryUrl);

            var remoteTagName = $"{rootUrl}{this.RepositoryName}:{this.Tag}";

            await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"tag {this.RepositoryName}:{this.Tag} {remoteTagName}"
                }
            );

            await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"push {remoteTagName}"
                }
            );

            if (logout)
            {
                this.LogDebug("Executing docker logout...");
                var result = await this.ExecuteDockerAsync(context, "logout", rootUrl, null);
                foreach (var m in result.Output.Concat(result.Error))
                {
                    if (!string.IsNullOrWhiteSpace(m))
                        this.Log(result.ExitCode == 0 ? MessageLevel.Debug : MessageLevel.Warning, m);
                }

                if (result.ExitCode != 0)
                    this.LogWarning("Docker logout failed with exit code " + result.ExitCode);
            }

            if (this.AttachToBuild)
                await this.AttachToBuildAsync(context, this.RepositoryName, this.Tag, this.ContainerSource);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Push ",
                    new Hilite(config[nameof(RepositoryName)] + ":" + config[nameof(Tag)]),
                    " Docker image"
                ),
                new RichDescription(
                    "to ",
                    new Hilite(config[nameof(ContainerSource)])
                )
            );
        }

        private async Task<bool> DockerLoginAsync(IOperationExecutionContext context, SDK.ContainerSourceInfo source)
        {
            if (string.IsNullOrWhiteSpace(source.CredentialName))
            {
                this.LogDebug($"No credentials are provided on container source \"{source.Name}\"; will not log in.");
                return false;
            }

            var bmContext = (IStandardContext)context;

            if (!(ResourceCredentials.Create("UsernamePassword", source.Name, bmContext.EnvironmentId, bmContext.ProjectId, true) is UsernamePasswordCredentials credentials))
                throw new ExecutionFailureException($"Credentials \"{source.Name}\" does not refer to a Username & Password credentials.");

            var server = GetServerName(source.RegistryUrl);
            this.LogDebug($"Executing docker login (user: {credentials.UserName}, server: {server})");
            var result = await this.ExecuteDockerAsync(context, "login", $"-u \"{credentials.UserName}\" -p \"{AH.Unprotect(credentials.Password)}\" {server}", null);

            foreach (var m in result.Output.Concat(result.Error))
            {
                if (!string.IsNullOrWhiteSpace(m))
                    this.Log(result.ExitCode == 0 ? MessageLevel.Debug : MessageLevel.Error, m);
            }

            if (result.ExitCode != 0)
                throw new ExecutionFailureException("Docker login failed with exit code " + result.ExitCode);

            return true;
        }

        private static string GetServerName(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ExecutionFailureException("Invalid container registry URL: " + url);

            return $"{uri.Host}:{uri.Port}{uri.AbsolutePath.TrimEnd('/')}/";
        }
    }
}
