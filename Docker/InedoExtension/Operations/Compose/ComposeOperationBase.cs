using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
    public abstract class ComposeOperationBase : DockerOperation
    {
        protected virtual string Command => null;

        [Required]
        [ScriptAlias("ProjectName")]
        [DisplayName("Project name")]
        public string ProjectName { get; set; }

        [Required]
        [ScriptAlias("ComposeYaml")]
        [DisplayName("Compose file (YAML)")]
        [Description("You can specify the path to the \"docker-compose.yaml\" or this can be the compose file contents, eg. $FileContents(docker-compose.yml) or $ConfigurationFileText(Integration, docker-compose.yaml)")]
        [FieldEditMode(FieldEditMode.Multiline)]
        [PlaceholderText("eg. $FileContents(docker-compose.yml)")]
        public string ComposeFileYaml { get; set; }

        [ScriptAlias("WorkingDirectory")]
        [DisplayName("Working directory")]
        public string WorkingDirectory { get; set; }

        [Category("Advanced")]
        [DefaultValue(false)]
        [ScriptAlias("Verbose")]
        public bool Verbose { get; set; }

        [Category("Advanced")]
        [ScriptAlias("AddArgs")]
        [DisplayName("Additional docker-compose arguments")]
        [FieldEditMode(FieldEditMode.Multiline)]
        public virtual IEnumerable<string> AddArgs { get; set; }

        [Required]
        [Category("Source")]
        [ScriptAlias("Source")]
        [DisplayName("Container source")]
        [SuggestableValue(typeof(ContainerSourceSuggestionProvider))]
        public string ContainerSource { get; set; }

        protected async Task RunDockerComposeAsync(IOperationExecutionContext context, params string[] args)
        {
            bool hasContainerSource = !string.IsNullOrWhiteSpace(this.ContainerSource);

            if (hasContainerSource)
                await this.LoginAsync(context, this.ContainerSource);
            try
            {
                await this.RunDockerComposeAsync(context, (IEnumerable<string>)args);
            }
            finally
            {
                if (hasContainerSource)
                    await this.LogoutAsync(context, this.ContainerSource);
            }
        }

        protected virtual async Task RunDockerComposeAsync(IOperationExecutionContext context, IEnumerable<string> args)
        {

            var fileOps = await context.Agent.TryGetServiceAsync<ILinuxFileOperationsExecuter>() ?? await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var procExec = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
            var workingDirectory = context.ResolvePath(string.IsNullOrWhiteSpace(this.WorkingDirectory) ? context.WorkingDirectory : this.WorkingDirectory);

            this.LogDebug($"Working directory: {workingDirectory}");
            await fileOps.CreateDirectoryAsync(workingDirectory);

            var isYamlPathSpecified = this.ComposeFileYaml.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
            string composeFileName = null;
            if (isYamlPathSpecified)
            {
                composeFileName = context.ResolvePath(this.ComposeFileYaml, workingDirectory);
            }
            else
            {
                await fileOps.CreateDirectoryAsync(fileOps.CombinePath(workingDirectory, "scripts"));
                composeFileName = fileOps.CombinePath(workingDirectory, "scripts", Guid.NewGuid().ToString("N") + ".yml");
            }


            var startInfo = new RemoteProcessStartInfo
            {
                FileName = "docker-compose",
                WorkingDirectory = workingDirectory
            };

            startInfo.AppendArgs(procExec, new[]
            {
                "--file",
                composeFileName,
                "--project-name",
                this.ProjectName,
                this.Verbose ? "--verbose" : null,
                "--no-ansi",
                this.Command
            }
            .Concat(this.AddArgs ?? new string[0])
            .Concat(args ?? new string[0])
            .Where(arg => arg != null));

            try
            {
                if (!isYamlPathSpecified)
                    await fileOps.WriteAllTextAsync(composeFileName, this.ComposeFileYaml);
               
                this.LogDebug($"Running command: {startInfo.FileName} {startInfo.Arguments}");

                int? exitCode;
                using (var process = procExec.CreateProcess(startInfo))
                {
                    process.OutputDataReceived += (s, e) => this.LogProcessOutput(e.Data);
                    process.ErrorDataReceived += (s, e) => this.LogProcessError(e.Data);
                    process.Start();
                    await process.WaitAsync(context.CancellationToken);
                    exitCode = process.ExitCode;
                }

                if (exitCode == 0)
                {
                    this.LogInformation("Process exit code indicates success.");
                    return;
                }

                this.LogError($"Process exit code indicates failure. ({AH.CoalesceString(exitCode, "(unknown)")})");
            }
            finally
            {
                if (!isYamlPathSpecified)
                {
                    await fileOps.DeleteFileAsync(composeFileName);
                }
            }

            // Command failed. Try to give a better error message if docker-compose isn't even installed.
            var verifyInstalledStartInfo = new RemoteProcessStartInfo
            {
                FileName = fileOps is ILinuxFileOperationsExecuter ? "/usr/bin/which" : "System32\\where.exe",
                Arguments = procExec.EscapeArg(startInfo.FileName),
                WorkingDirectory = workingDirectory
            };

            if (fileOps is ILinuxFileOperationsExecuter)
                verifyInstalledStartInfo.Arguments = "-- " + verifyInstalledStartInfo.Arguments;
            else
                verifyInstalledStartInfo.FileName = fileOps.CombinePath(await procExec.GetEnvironmentVariableValueAsync("SystemRoot"), verifyInstalledStartInfo.FileName);

            using (var process = procExec.CreateProcess(verifyInstalledStartInfo))
            {
                // Don't care about output.
                process.Start();
                await process.WaitAsync(context.CancellationToken);

                // 0 = file exists, anything other than 0 or 1 = error trying to run which/where.exe
                if (process.ExitCode == 1)
                {
                    this.LogWarning("Is docker-compose installed and in the PATH?");
                }
            }
        }

        protected override void LogProcessError(string text) =>this.LogDebug(text);
    }
}
