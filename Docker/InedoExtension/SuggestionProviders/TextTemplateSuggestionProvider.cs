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
        public async Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            using (var raft = RaftRepository.OpenRaft(null, OpenRaftOptions.ReadOnly | OpenRaftOptions.OptimizeLoadTime))
#pragma warning restore CS0618 // Type or member is obsolete
            {
                return from t in await raft.GetRaftItemsAsync(RaftItemType.TextTemplate)
                       select t.ItemName;
            }
        }
    }
}
