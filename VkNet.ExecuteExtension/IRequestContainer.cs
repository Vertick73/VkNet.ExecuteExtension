using System;

namespace VkNet.ExecuteExtension
{
    public interface IRequestContainer<T> : IDisposable
    {
        public int TotalRequestsWeight { get; }
        public void Add(T item);
        public void TransferRequests(IVkApiClient<T> vkApiClient);
        public DateTime GetLastAddTime();
        public DateTime GetFirstAddTime();
    }
}