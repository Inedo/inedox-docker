using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Web;

namespace Inedo.Extensions.Docker.SuggestionProviders
{
    public sealed class TextTemplateSuggestionProvider : ISuggestionProvider
    {
        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
            return Task.FromResult(from t in SDK.GetRaftItems(RaftItemType.TextTemplate, config.EditorContext)
                                   select t.Name);
        }
    }
}
