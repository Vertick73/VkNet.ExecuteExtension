using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VkNet.Exception;
using VkNet.Utils;

namespace VkNet.ExecuteExtension
{
    public class ExecuteManager : IAsyncDisposable
    {
        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        private readonly CancellationTokenSource _cts;
        private readonly Task _executeHandlerTask;
        private List<Task> _executeRunTasks = new();
        private readonly IRequestContainer<MethodData> _requestContainer;

        public ExecuteManager(CancellationTokenSource cts, IRequestContainer<MethodData> requestContainer,
            IEnumerable<IVkApiClient<MethodData>> apiClients)
        {
            _cts = cts;
            _requestContainer = requestContainer;
            ApiClients = apiClients;
            _executeHandlerTask = Task.Run(() => ExecuteCycleTask(_cts.Token));
        }

        public TimeSpan CheckDelay { get; set; } = TimeSpan.FromMilliseconds(100);
        public TimeSpan MaxWaitingTime { get; set; } = TimeSpan.FromMilliseconds(5000);
        public TimeSpan PendingTime { get; set; } = TimeSpan.FromMilliseconds(1000);
        public IEnumerable<IVkApiClient<MethodData>> ApiClients { get; set; }

        public async ValueTask DisposeAsync()
        {
            _cts?.Dispose();
            _requestContainer?.Dispose();
            await _executeHandlerTask.ConfigureAwait(false);
        }

        private async Task ExecuteCycleTask(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                    foreach (var api in ApiClients)
                    {
                        while (_requestContainer.TotalRequestsWeight < api.MaxExecuteWeight && !IsTimeout())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await Task.Delay(CheckDelay, cancellationToken);
                        }

                        _requestContainer.TransferRequests(api);
                        _ = ExecuteRun(api, cancellationToken).ContinueWith(ErrorCheck).ConfigureAwait(false);
                    }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ErrorCheck(Task t)
        {
            if (t.IsFaulted)
            {
                //log
            }
        }

        private bool IsTimeout()
        {
            if (_requestContainer.TotalRequestsWeight == 0) return false;
            return DateTime.Now - _requestContainer.GetLastAddTime() > PendingTime ||
                   DateTime.Now - _requestContainer.GetFirstAddTime() > MaxWaitingTime;
        }

        public Task<VkResponse> AddToExecuteAsync(string methodName, VkParameters parameters, int weight = 1)
        {
            var tcs = new TaskCompletionSource<VkResponse>();
            var request = new MethodData
                {ExecuteWeight = weight, Parameters = parameters, Name = methodName, Task = tcs};
            _requestContainer.Add(request);
            return tcs.Task;
        }

        public async Task ExecuteRun(IVkApiClient<MethodData> vkApiClient, CancellationToken cancellationToken)
        {
            if (vkApiClient.RequestsToExecute.Count == 0) return;
            var executeCode = new StringBuilder("var out = [];");
            var index = 0;
            var requests = new List<MethodData>(vkApiClient.RequestsToExecute);
            vkApiClient.RequestsReset();
            foreach (var methodData in requests)
            {
                methodData.ExecuteIndex = index++;
                executeCode.Append(
                    $"out.push({{\"id\":{methodData.ExecuteIndex}, \"res\":API.{methodData.Name}({JsonConvert.SerializeObject(methodData.Parameters, SerializerSettings)})}});");
            }

            executeCode.Append("return out;");
            try
            {
                var rawRes = await vkApiClient.VkApi.Execute.ExecuteAsync(executeCode.ToString())
                    .ConfigureAwait(false); //???
                cancellationToken.ThrowIfCancellationRequested();
                var res = rawRes.ToListOf(x => x["res"]);
                for (var i = 0; i < requests.Count; i++) requests[i].Task.SetResult(res[i]);
            }
            catch (TooManyRequestsException)
            {
                foreach (var methodData in requests) _requestContainer.Add(methodData); //fix return
            }
            catch (ExecuteException e)
            {
                var rawResponse = JArray.Parse(e.Response.Value.ToString());
                var clearResponse = rawResponse.Select(x => x.SelectToken("res")).ToArray();
                var errId = 0;
                for (var i = 0; i < clearResponse.Length; i++)
                    if (clearResponse[i].Type == JTokenType.Boolean)
                        requests[i].Task.SetException(e.InnerExceptions[errId++]);
                    else
                        requests[i].Task.SetResult(new VkResponse(clearResponse[i]));
            }
            catch (OperationCanceledException)
            {
                foreach (var methodData in requests) methodData.Task.SetCanceled(cancellationToken);
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().Wait();
        }
    }
}