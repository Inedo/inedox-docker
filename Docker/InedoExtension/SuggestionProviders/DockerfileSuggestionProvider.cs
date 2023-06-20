using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Web;

namespace Inedo.Extensions.Docker.SuggestionProviders
{
    public sealed class DockerfileSuggestionProvider : ISuggestionProvider
    {
        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
            var items = new List<string>();
            try
            {
#warning Updated to use actual enum type RaftItemType.BuildFile once upgraded to SDK 2.4
                items.AddRange(from t in SDK.GetRaftItems((RaftItemType)11, config.EditorContext)
                               where t.Name.EndsWith("dockerfile", System.StringComparison.OrdinalIgnoreCase)
                               select t.Name);
            }
            catch
            {
                //ignore, here to use new raft item type, when exists
            }

            items.AddRange(from t in SDK.GetRaftItems(RaftItemType.TextTemplate, config.EditorContext)
                           select t.Name);
            return Task.FromResult(items.AsEnumerable());
        }
    }
}
