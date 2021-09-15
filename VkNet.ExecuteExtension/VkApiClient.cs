using System.Collections.Generic;
using VkNet.Abstractions;
using VkNet.Model;

namespace VkNet.ExecuteExtension
{
    public class VkApiClient<T> : IVkApiClient<T> where T : MethodData
    {
        private int _maxExecuteWeight = 25;

        public VkApiClient(IVkApi vkApi)
        {
            VkApi = vkApi;
        }

        public VkApiClient(string token)
        {
            VkApi = new VkApi();
            VkApi.Authorize(new ApiAuthParams {AccessToken = token});
        }

        public IVkApi VkApi { get; }

        public int MaxExecuteWeight
        {
            get => _maxExecuteWeight;
            set
            {
                if (value < 1 || value > 25) throw new System.Exception("MaxExecuteWeight must be >0 and <=25");
                AvailableWeight += value - _maxExecuteWeight;
                _maxExecuteWeight = value;
            }
        }

        //public int CurrentWeight { get; private set; }
        public int AvailableWeight { get; private set; } = 25;
        public IList<T> RequestsToExecute { get; } = new List<T>();

        public void RequestsReset()
        {
            RequestsToExecute.Clear();
            //CurrentWeight = 0;
            AvailableWeight = _maxExecuteWeight;
        }

        public bool TryAdd(T request)
        {
            if (request.ExecuteWeight > AvailableWeight) return false;
            RequestsToExecute.Add(request);
            AvailableWeight -= request.ExecuteWeight;
            return true;
        }

    }
}