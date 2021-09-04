using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VkNet.ExecuteExtension
{
    public class RequestContainer<T> : IRequestContainer<T> where T : MethodData
    {
        private readonly int _maxRequstResultWeight;
        private DateTime _lastAddTime;
        private readonly ConcurrentQueue<T>[] _requestStorage;

        public RequestContainer(int maxRequstResultWeight = 25)
        {
            _maxRequstResultWeight = maxRequstResultWeight;
            _requestStorage = new ConcurrentQueue<T>[maxRequstResultWeight];
            for (var i = 0; i < maxRequstResultWeight; i++) _requestStorage[i] = new ConcurrentQueue<T>();
        }

        public int TotalRequestsWeight { private set; get; }

        public virtual void Add(T item)
        {
            if (item.ExecuteWeight > _maxRequstResultWeight) throw new System.Exception("ExecuteWeight is too big");

            if (item.ExecuteWeight <= 0) throw new System.Exception("ExecuteWeight must be greater than 0");
            _requestStorage[_maxRequstResultWeight - item.ExecuteWeight].Enqueue(item);
            _lastAddTime = DateTime.Now;
            TotalRequestsWeight += item.ExecuteWeight;
        }

        public void TransferRequests(IVkApiClient<T> vkApiClient)
        {
            var unsuitableData = new Stack<T>();
            var remainingResultsWeight = vkApiClient.MaxExecuteWeight - vkApiClient.CurrentWeight;
            var requestQueues = _requestStorage.Where(x =>
                !x.IsEmpty && x.TryPeek(out var peakData) && peakData.ExecuteWeight <= remainingResultsWeight);
            foreach (var requestQueue in requestQueues)
            {
                while (!requestQueue.IsEmpty)
                    if (requestQueue.TryDequeue(out var data))
                    {
                        if (data.ExecuteWeight > remainingResultsWeight)
                        {
                            unsuitableData.Push(data);
                            break;
                        }

                        if (vkApiClient.TryAdd(data))
                        {
                            remainingResultsWeight -= data.ExecuteWeight;
                            TotalRequestsWeight -= data.ExecuteWeight;
                        }
                        else
                        {
                            unsuitableData.Push(data);
                        }
                    }

                while (unsuitableData.Count > 0) requestQueue.Enqueue(unsuitableData.Pop());

                if (remainingResultsWeight == 0) break;
            }
        }

        public DateTime GetLastAddTime()
        {
            return _lastAddTime;
        }

        public DateTime GetFirstAddTime()
        {
            var res = _requestStorage
                .Select(queue => queue.TryPeek(out var data) ? (ok: true, data) : (ok: false, null))
                .Where(t => t.ok)
                .Select(t => t.data.AddTime).Min();
            return res;
        }

        public void Dispose()
        {
            var toDispose = _requestStorage
                .Select(queue => queue.TryPeek(out var data) ? (ok: true, data) : (ok: false, null))
                .Where(t => t.ok).Select(t => t.data);
            foreach (var item in toDispose) item.Task.SetCanceled();
        }
    }
}