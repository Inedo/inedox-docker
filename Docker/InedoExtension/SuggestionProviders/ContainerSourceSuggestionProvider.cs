using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Extensions.SecureResources;
using Inedo.Web;

namespace Inedo.Extensions.Docker.SuggestionProviders
{
    public sealed class ContainerSourceSuggestionProvider : ISuggestionProvider
    {
        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
            return Task.FromResult(from resource in SDK.GetSecureResources()
                                   where resource.InstanceType == typeof(DockerRepository)
                                   select resource.Name);
        }
    }
}
