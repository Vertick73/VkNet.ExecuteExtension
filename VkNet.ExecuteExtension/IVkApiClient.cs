using System.Collections.Generic;
using VkNet.Abstractions;

namespace VkNet.ExecuteExtension
{
    public interface IVkApiClient<T>
    {
        public IVkApi VkApi { get; }
        public int MaxExecuteWeight { get; set; }
        public int AvailableWeight { get; }
        public IList<T> RequestsToExecute { get; }
        public bool TryAdd(T request);
        public void RequestsReset();
    }
}