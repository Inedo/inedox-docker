using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.Docker.SuggestionProviders
{
    public sealed class ContainerSourceSuggestionProvider : ISuggestionProvider
    {
        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
            return Task.FromResult(SDK.GetContainerSources().Select(source => source.ResourceInfo.Name));
        }
    }
}
