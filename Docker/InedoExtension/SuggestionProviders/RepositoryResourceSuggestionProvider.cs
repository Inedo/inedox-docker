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
                               where resource.InstanceType == typeof(DockerRepository)
                               select resource.Name);
    }
}
