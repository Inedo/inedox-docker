using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Inedo.Extensibility;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.SecureResources;
using Inedo.Web;

namespace Inedo.Extensions.Docker.SuggestionProviders;

internal sealed class RepositoryResourceSuggestionProvider : ISuggestionProvider
{
    public IAsyncEnumerable<string> GetSuggestionsAsync(IComponentConfiguration config, CancellationToken cancellationToken)
    {
        return (from resource in SDK.GetSecureResources(config.EditorContext as IResourceResolutionContext)
                where resource.InstanceType == typeof(DockerRepository)
                select resource.Name).ToAsyncEnumerable();
    }
}
