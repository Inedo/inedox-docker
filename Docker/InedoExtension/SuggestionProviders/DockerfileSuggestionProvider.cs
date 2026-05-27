using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Inedo.Extensibility;
using Inedo.Extensibility.RaftRepositories;
using Inedo.Web;

namespace Inedo.Extensions.Docker.SuggestionProviders;

internal sealed class DockerfileSuggestionProvider : ISuggestionProvider
{
    public IAsyncEnumerable<string> GetSuggestionsAsync(IComponentConfiguration config, CancellationToken cancellationToken)
    {
        var items = new List<string>();
        try
        {
            items.AddRange(from t in SDK.GetRaftItems(RaftItemType.BuildFile, config.EditorContext)
                           where t.Name.EndsWith("dockerfile", System.StringComparison.OrdinalIgnoreCase)
                           select t.Name);
        }
        catch
        {
            //ignore, here to use new raft item type, when exists
        }

        items.AddRange(from t in SDK.GetRaftItems(RaftItemType.TextFile, config.EditorContext)
                       select t.Name);

        return items.ToAsyncEnumerable();
    }
}
