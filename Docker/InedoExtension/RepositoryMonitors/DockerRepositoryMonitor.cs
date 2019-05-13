using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility.RepositoryMonitors;
using Inedo.Serialization;
using Newtonsoft.Json;

namespace Inedo.Extensions.Docker.RepositoryMonitors
{
    [DisplayName("Docker")]
    [Description("Monitors a Docker repository for new versions of tagged images.")]
    public sealed class DockerRepositoryMonitor : RepositoryMonitor
    {
        [Required]
        [Persistent]
        [DisplayName("Image name")]
        public string ImageName { get; set; }

        public async override Task<IReadOnlyDictionary<string, RepositoryCommit>> GetCurrentCommitsAsync(IRepositoryMonitorContext context)
        {
            string repositoryUrl;
            int firstSlash = this.ImageName.IndexOf('/');
            if (firstSlash == -1 || this.ImageName.IndexOfAny(new[] { ':', '.' }, 0, firstSlash) == -1)
                repositoryUrl = "https://registry.hub.docker.com/v2/" + Uri.EscapeUriString(this.ImageName);
            else
                repositoryUrl = "https://" + this.ImageName.Substring(0, firstSlash) + "/v2/" + Uri.EscapeUriString(this.ImageName.Substring(firstSlash + 1));

            using (var client = new HttpClient())
            {
                string[] tags;
                using (var response = await client.GetAsync(repositoryUrl + "/tags/list", context.CancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync();
                    tags = JsonConvert.DeserializeAnonymousType(body, new { tags = new string[0] }).tags;
                }

                var results = new Dictionary<string, RepositoryCommit>();

                foreach (var tag in tags)
                {
                    using (var response = await client.GetAsync(repositoryUrl + "/manifests/" + Uri.EscapeUriString(tag), context.CancellationToken))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            this.LogWarning($"Request for tag {tag} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                            continue;
                        }

                        var digest = response.Headers.GetValues("Docker-Content-Digest").FirstOrDefault();
                        if (digest == null)
                        {
                            this.LogWarning($"Missing Docker-Content-Digest for tag {tag}");
                            continue;
                        }

                        results[tag] = new DockerRepositoryCommit { Digest = digest };
                    }
                }

                return results;
            }
        }

        public override RichDescription GetDescription()
        {
            return new RichDescription("Docker image ", new Hilite(this.ImageName));
        }
    }
}
