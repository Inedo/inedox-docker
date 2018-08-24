using System.Collections.Generic;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.Docker.SuggestionProviders
{
    public sealed class SignalSuggestionProvider : ISuggestionProvider
    {
        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
            // The most common Linux signals used to terminate processes.
            return Task.FromResult<IEnumerable<string>>(new[]
            {
                "SIGKILL",
                "SIGTERM",
                "SIGINT",
                "SIGHUP",
                "SIGQUIT"
            });
        }
    }
}
