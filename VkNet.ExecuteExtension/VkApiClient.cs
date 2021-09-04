using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VkNet.Abstractions;
using VkNet.Exception;
using VkNet.Model;
using VkNet.Utils;

namespace VkNet.ExecuteExtension
{
    public class VkApiClient<T> : IVkApiClient<T> where T : MethodData
    {
        protected static readonly JsonSerializerSettings serializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

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

                _maxExecuteWeight = value;
            }
        }

        public int CurrentWeight { get; private set; }
        public IList<T> RequestsToExecute { get; } = new List<T>();

        public void RequestsReset()
        {
            RequestsToExecute.Clear();
            CurrentWeight = 0;
        }

        public bool TryAdd(T request)
        {
            if (request.ExecuteWeight > _maxExecuteWeight - CurrentWeight) return false;
            RequestsToExecute.Add(request);
            CurrentWeight += request.ExecuteWeight;
            return true;
        }

        public async Task ExecuteRun(IRequestContainer<T> requestContainer, CancellationToken cancellationToken)
        {
            if (RequestsToExecute.Count == 0) return;
            var executeCode = new StringBuilder("var out = [];");
            var index = 0;
            foreach (var methodData in RequestsToExecute)
            {
                methodData.ExecuteIndex = index++;
                executeCode.Append(
                    $"out.push({{\"id\":{methodData.ExecuteIndex}, \"res\":API.{methodData.Name}({JsonConvert.SerializeObject(methodData.Parameters, serializerSettings)})}});");
            }

            executeCode.Append("return out;");
            try
            {
                var rawRes = await VkApi.Execute.ExecuteAsync(executeCode.ToString()).ConfigureAwait(false); //???
                cancellationToken.ThrowIfCancellationRequested();
                var res = rawRes.ToListOf(x => x["res"]);
                for (var i = 0; i < RequestsToExecute.Count; i++) RequestsToExecute[i].Task.SetResult(res[i]);
            }
            catch (TooManyRequestsException)
            {
                foreach (var methodData in RequestsToExecute) requestContainer.Add(methodData);
            }
            catch (ExecuteException e)
            {
                var rawResponse = JArray.Parse(e.Response.Value.ToString());
                var clearResponse = rawResponse.Select(x => x.SelectToken("res")).ToArray();
                var errId = 0;
                for (var i = 0; i < clearResponse.Length; i++)
                    if (clearResponse[i].Type == JTokenType.Boolean)
                        RequestsToExecute[i].Task.SetException(e.InnerExceptions[errId++]);
                    else
                        RequestsToExecute[i].Task.SetResult(new VkResponse(clearResponse[i]));
            }
            catch (OperationCanceledException)
            {
                foreach (var methodData in RequestsToExecute) methodData.Task.SetCanceled(cancellationToken);
            }
        }
    }
}