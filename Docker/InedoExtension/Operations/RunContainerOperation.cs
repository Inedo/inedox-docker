﻿using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Docker.SuggestionProviders;
using Inedo.Web;

#nullable enable

namespace Inedo.Extensions.Docker.Operations;

[ScriptAlias("Run-Container")]
[ScriptNamespace("Docker")]
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
    [DisplayName("Repository URL")]
    [ScriptAlias("RepositoryUrl")]
    [PlaceholderText("ex: mcr.microsoft.com/mssql/server")]
    [Description("Repository URL is a public Docker Repository to pull an image without the need to login.  This overrides the Repository parameter.  ex: mcr.microsoft.com/mssql/server")]
    public string? RepositoryUrl { get; set; }

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
    public bool? RunInBackground { get; set; }
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
        if (string.IsNullOrEmpty(this.RepositoryResourceName) && string.IsNullOrWhiteSpace(this.RepositoryUrl))
            throw new ExecutionFailureException($"A RepositoryResourceName or a RepositoryUrl was not specified.");
        if (string.IsNullOrEmpty(this.Tag))
            throw new ExecutionFailureException($"A Tag was not specified.");
        if (!string.IsNullOrEmpty(this.DockerRunConfig) && string.IsNullOrEmpty(this.DockerRunConfigInstance))
            throw new ExecutionFailureException($"An Instance is required when specifying a Docker Run Config.");

        var repoResource = string.IsNullOrWhiteSpace(this.RepositoryUrl) ? this.CreateRepository(context, this.RepositoryResourceName, this.LegacyRepositoryName) : null;
        var repository = AH.NullIf(this.RepositoryUrl, string.Empty) ?? repoResource?.GetRepository(context);

        if (string.IsNullOrEmpty(repository))
            throw new ExecutionFailureException($"Docker repository \"{this.RepositoryResourceName}\" has an unexpected name.");

        if (string.IsNullOrEmpty(this.ContainerName))
            this.ContainerName = repository.Split('/').Last();


        var repositoryAndTag = $"{repository}:{this.Tag}".ToLower();

        var client = await DockerClientEx.CreateAsync(this, context);

        var dockerRunText = await getDockerRunTextAsync();

        if (repoResource != null)
            await client.LoginAsync(repoResource);
        try
        {
            await client.DockerAsync($"pull {client.EscapeArg(repositoryAndTag)}");

            var runArgs = new StringBuilder($"run --name {client.EscapeArg(this.ContainerName)}");
            if (!string.IsNullOrEmpty(dockerRunText))
                runArgs.Append($" {dockerRunText}");
            if (this.RemoveOnExit)
                runArgs.Append(" --rm");
            if (this.RunInBackground ?? true)
                runArgs.Append(" -d");
            if (!string.IsNullOrWhiteSpace(AdditionalArguments))
                runArgs.Append($" {this.AdditionalArguments}");
            runArgs.Append($" {client.EscapeArg(repositoryAndTag)}");

            await client.DockerAsync(runArgs.ToString(), true);
        }
        finally
        {
            if (repoResource != null)
                await client.LogoutAsync();
        }

        async Task<string?> getDockerRunTextAsync()
        {
            if (string.IsNullOrEmpty(this.DockerRunConfig))
                return null;

            var deployer = (await context.TryGetServiceAsync<IConfigurationFileDeployer>())
                ?? throw new ExecutionFailureException("Configuration files are not supported in this context.");
            var fileOps = await context.TryGetServiceAsync<IFileOperationsExecuter>();
            var isLinux = (fileOps?.DirectorySeparator ?? '/') == '/';

            using var writer = new StringWriter();
            if (!await deployer.WriteAsync(writer, this.DockerRunConfig, this.DockerRunConfigInstance, this))
                throw new ExecutionFailureException("Error reading Docker Run Config.");

            var configText = new StringBuilder();
            foreach (var config in Regex.Split(writer.ToString(), @"\r?\n", RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(config))
                    continue;

                if (config.StartsWith("-v ", StringComparison.OrdinalIgnoreCase))
                {
                    configText.Append($"-v {escapeParameter(config, 3, ':', true)} ");
                }
                else if (config.StartsWith("-l ", StringComparison.OrdinalIgnoreCase))
                {
                    configText.Append($"-l {escapeParameter(config, 3, ':', true)} ");
                }
                else if (config.StartsWith("-e ", StringComparison.OrdinalIgnoreCase))
                {
                    configText.Append($"-e {escapeParameter(config, 3, '=')} ");
                }
                else if (config.StartsWith("-p ", StringComparison.OrdinalIgnoreCase))
                {
                    configText.Append($"{config} ");
                }
                else if (config.StartsWith("--cpus=", StringComparison.OrdinalIgnoreCase))
                {
                    configText.Append($"--cpus={escapeString(config, 7)} ");
                }
                else if (config.StartsWith("--memory=", StringComparison.OrdinalIgnoreCase))
                {
                    configText.Append($"--memory={escapeString(config, 9)} ");
                }
                else if (config.StartsWith("--gpus ", StringComparison.OrdinalIgnoreCase))
                {
                    configText.Append($"--gpus {escapeString(config, 7)} ");
                }
                else
                {
                    throw new ExecutionFailureException($"Invalid Docker Run Configuration '{config.Split(' ')[0]}'");
                }

            }
            return configText.ToString();

            string escapeParameter(string config, int startIndex, char splitOn, bool onlyOnWhitespace = false)
            {
                var param = verifyCharacters(config, startIndex);
                var splitIndex = param.IndexOf(splitOn);

                if (splitIndex < 0)
                    return param;
                
                var key = param.Substring(0, splitIndex);
                var value = param.Substring(splitIndex + 1);

                if (value == null || AH.ParseInt(value) != null ||  value.StartsWith("\""))
                    return param;

                if(onlyOnWhitespace && !Regex.IsMatch(value, @".*\s+.*"))
                    return param;

                return $"{key}{splitOn}\"{Regex.Replace(value, @"(?<!\\)((?:\\\\)*)("")", "$1\\\"")}\"";
            }

            string escapeString(string config, int startIndex)
            {
                var param = verifyCharacters(config, startIndex);

                if (Regex.IsMatch(param, @".*\s+.*") && !Regex.IsMatch(param, @"[""'].*\s.*[""']"))
                    return $"\"{Regex.Replace(param, @"(?<!\\)((?:\\\\)*)("")", "$1\\\"")}\"";

                return verifyCharacters(config, startIndex);
            }

            string verifyCharacters(string config, int startIndex)
            {
                var param = config.Substring(startIndex);

                if (param != null && (
                    Regex.IsMatch(param, @"(?<!\\)((?:\\\\)*)([\^\$\|\?\&\!])") 
                    || (Regex.IsMatch(param, @".*\s+.*") && !Regex.IsMatch(param, @"[""'].*\s.*[""']"))
                ))
                    this.LogInformation($"\"{config}\" contains a special character that has not been escaped.");

                return param ?? string.Empty;
            }
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
