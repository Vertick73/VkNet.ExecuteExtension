using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VkNet.Abstractions;
using VkNet.Exception;
using VkNet.Model;
using VkNet.Utils;

namespace VkNet.ExecuteExtension
{
    public class ExecuteClient : IAsyncDisposable
    {
        private static readonly JsonSerializerSettings serializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        private readonly CancellationTokenSource _cts;
        private readonly Task _executeHandlerTask;
        private readonly ConcurrentQueue<MethodData> _methodsToExecute = new();
        private readonly IVkApi _vkApi;
        private DateTime? firstAddTime = DateTime.Now;
        private DateTime? lastAddTime = DateTime.Now;

        public ExecuteClient(string token, CancellationTokenSource cts)
        {
            _vkApi = new VkApi();
            _vkApi.Authorize(new ApiAuthParams
            {
                AccessToken = token
            });
            _cts = cts;
            _executeHandlerTask = Task.Run(() => ExecuteCycleTask(_cts.Token));
        }

        public TimeSpan CheckDelay { get; set; } = TimeSpan.FromMilliseconds(100);
        public int maxExecuteCount { get; set; } = 25;
        public TimeSpan MaxWaitingTime { get; set; } = TimeSpan.FromMilliseconds(5000);
        public TimeSpan PendingTime { get; set; } = TimeSpan.FromMilliseconds(1000);

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await _executeHandlerTask.ConfigureAwait(false);
        }

        private async Task ExecuteCycleTask(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_methodsToExecute.Count >= maxExecuteCount || !_methodsToExecute.IsEmpty && IsTimeout())
                    {
                        var methodsDataChunck = new List<MethodData>();
                        MethodData req;

                        for (var i = 0; i < maxExecuteCount; i++)
                            if (_methodsToExecute.TryDequeue(out req))
                                methodsDataChunck.Add(req);
                        ExecuteRun(methodsDataChunck, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(CheckDelay);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                foreach (var methodData in _methodsToExecute) methodData.Task.SetCanceled();
            }
        }

        private bool IsTimeout()
        {
            return DateAndTime.Now - lastAddTime > PendingTime || DateAndTime.Now - firstAddTime > MaxWaitingTime
                ? true
                : false;
        }

        private async void ExecuteRun(List<MethodData> methods, CancellationToken cancellationToken)
        {
            var executeCode = new StringBuilder("var out = [];");
            for (var i = 0; i < methods.Count; i++)
            {
                var methodData = methods[i];
                methodData.ExecuteIndex = i;
                executeCode.Append(
                    $"out.push({{\"id\":{methodData.ExecuteIndex}, \"res\":API.{methodData.Name}({JsonConvert.SerializeObject(methodData.Parameters, serializerSettings)})}});");
            }

            executeCode.Append("return out;");
            try
            {
                var rawRes = await _vkApi.Execute.ExecuteAsync(executeCode.ToString()).ConfigureAwait(false); //??
                cancellationToken.ThrowIfCancellationRequested();
                var res = rawRes.ToListOf(x => x["res"]);
                for (var i = 0; i < methods.Count; i++) methods[i].Task.SetResult(res[i]);
            }
            catch (TooManyRequestsException)
            {
                foreach (var methodData in methods) _methodsToExecute.Enqueue(methodData);
            }
            catch (ExecuteException e)
            {
                var rawResponse = JArray.Parse(e.Response.Value.ToString());
                var clearResponse = rawResponse.Select(x => x.SelectToken("res")).ToArray();
                var errId = 0;
                for (var i = 0; i < clearResponse.Length; i++)
                    if (clearResponse[i].Type == JTokenType.Boolean)
                        methods[i].Task.SetException(e.InnerExceptions[errId++]);
                    else
                        methods[i].Task.SetResult(new VkResponse(clearResponse[i]));
            }
            catch (OperationCanceledException)
            {
                foreach (var methodData in methods) methodData.Task.SetCanceled();
            }
        }

        public Task<VkResponse> AddToExecuteAsync(string methodName, VkParameters parameters)
        {
            var tcs = new TaskCompletionSource<VkResponse>();
            if (_methodsToExecute.IsEmpty)
            {
                firstAddTime=DateTime.Now;
            }
            _methodsToExecute.Enqueue(new MethodData
            {
                Name = methodName,
                Parameters = parameters,
                Task = tcs
            });
            lastAddTime=DateTime.Now;
            return tcs.Task;
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().Wait();
        }
    }
}