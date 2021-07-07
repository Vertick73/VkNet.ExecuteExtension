using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VkNet.Utils;

namespace VkNet.ExecuteExtension
{
    public class ExecuteManager
    {
        private List<ExecuteClient> _executeClients = new List<ExecuteClient>();
        private int _lastClientExecute;
        public ExecuteManager(CancellationTokenSource cts, params string[] tokens )
        {
            foreach (var token in tokens)
            {
                _executeClients.Add(new ExecuteClient(token,cts));
            }
        }

        public virtual Task<VkResponse> AddToExecuteAsync(string methodName, VkParameters parameters)
        {
            return _executeClients[_lastClientExecute++ % _executeClients.Count]
                .AddToExecuteAsync(methodName, parameters);
        }
    }
}
