using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.SecureResources;
using Inedo.Web;

namespace Inedo.Extensions.Docker.SuggestionProviders;

internal sealed class RepositoryResourceSuggestionProvider : ISuggestionProvider
{
    public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
    {
        return Task.FromResult(from resource in SDK.GetSecureResources(config.EditorContext as IResourceResolutionContext)

#pragma warning disable CS0618 
                            // where resource.InstanceType == typeof(DockerRepository)
                               where resource.InstanceType == typeof(ContainerSource) || resource.InstanceType.Name == "DockerRepository"
#pragma warning restore CS0618
                               select resource.Name);
    }
}
