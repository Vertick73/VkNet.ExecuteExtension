using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VkNet.Abstractions;
using VkNet.Abstractions.Authorization;
using VkNet.Exception;
using VkNet.Infrastructure;
using VkNet.Utils;
using VkNet.Utils.AntiCaptcha;
using ILogger = NLog.ILogger;

namespace VkNet.ExecuteExtension
{
    public class VkApiExecute : VkApi, IVkApi
    {
        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        private readonly ConcurrentQueue<CallRequest> _callRequests = new();

        private readonly CancellationTokenSource _cts;
        private readonly Task _executeHandlerTask;
        private readonly ILogger _executeLogger;
        private int _currentRequestsWeight;

        private long _lastAddTimeTicks;
        private int _maxExecute = 25;

        public VkApiExecute(ILogger<VkApi> logger, ICaptchaSolver captchaSolver = null,
            IAuthorizationFlow authorizationFlow = null) : base(logger, captchaSolver, authorizationFlow)
        {
            _cts = new CancellationTokenSource();
            _executeHandlerTask = Task.Run(() => ExecuteCycleTask(_cts.Token));
        }

        public VkApiExecute(ILogger executeLogger, ILogger<VkApi> logger = null, ICaptchaSolver captchaSolver = null,
            IAuthorizationFlow authorizationFlow = null) : this(logger, captchaSolver, authorizationFlow)
        {
            _executeLogger = executeLogger;
        }

        public VkApiExecute(IServiceCollection serviceCollection = null) : base(serviceCollection)
        {
            _cts = new CancellationTokenSource();
            _executeHandlerTask = Task.Run(() => ExecuteCycleTask(_cts.Token));
        }

        private DateTime _lastAddTime
        {
            get => new(Interlocked.Read(ref _lastAddTimeTicks));
            set => Interlocked.Exchange(ref _lastAddTimeTicks, value.Ticks);
        }

        public int MaxExecute
        {
            get => _maxExecute;
            set
            {
                if (value < 1 || value > 25)
                    throw new ArgumentException(@"Value must be positive and <=25", nameof(value));
                Interlocked.Exchange(ref _maxExecute, value);
            }
        }

        public TimeSpan CheckDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public TimeSpan MaxWaitingTime { get; set; } = TimeSpan.FromMilliseconds(3000);
        public TimeSpan PendingTime { get; set; } = TimeSpan.FromMilliseconds(1000);

        //[MethodImpl(MethodImplOptions.NoInlining)]
        public new VkResponse Call(string methodName, VkParameters parameters, bool skipAuthorization = false)
        {
            if (methodName == "execute") return base.Call(methodName, parameters, skipAuthorization);

            var tcs = new TaskCompletionSource<VkResponse>();
            var request = new CallRequest
                { Parameters = parameters, Name = methodName, Task = tcs, AddTime = DateTime.Now };
            _callRequests.Enqueue(request);
            Interlocked.Add(ref _currentRequestsWeight, request.ExecuteWeight);
            _lastAddTime = DateTime.Now;
            //_executeLogger?.Debug($"Добавление метода {methodName} с весом {request.ExecuteWeight} в очередь вызова, с параметрами {string.Join(",", parameters.Where(x => x.Key != Constants.AccessToken).Select(x => $"{x.Key}={x.Value}"))}");
            return tcs.Task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private async Task ExecuteCycleTask(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    if (_currentRequestsWeight >= _maxExecute || IsTimeout())
                    {
                        var requestsToExecute = new List<CallRequest>();
                        var remainingFreeWeight = _maxExecute;
                        while (_callRequests.TryPeek(out var peak) && peak.ExecuteWeight <= remainingFreeWeight)
                            if (_callRequests.TryDequeue(out var res))
                            {
                                Interlocked.Add(ref _currentRequestsWeight, -res.ExecuteWeight);
                                requestsToExecute.Add(res);
                                remainingFreeWeight -= res.ExecuteWeight;
                            }

                        _ = ExecuteRun(requestsToExecute, cancellationToken).ConfigureAwait(false);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(CheckDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private bool IsTimeout()
        {
            if (_callRequests.IsEmpty) return false;
            var minTime = DateTime.Now - MaxWaitingTime;
            return
                DateTime.Now - _lastAddTime >
                PendingTime || _callRequests.Any(x => x.AddTime < minTime);
        }

        private async Task ExecuteRun(IList<CallRequest> callRequests, CancellationToken cancellationToken)
        {
            if (callRequests.Count == 0) return;
            var executeCode = new StringBuilder("var out = [];");
            var index = 0;
            foreach (var callRequest in callRequests)
            {
                callRequest.ExecuteIndex = index++;
                executeCode.Append(
                    $"out.push({{\"id\":{callRequest.ExecuteIndex}, \"res\":API.{callRequest.Name}({JsonConvert.SerializeObject(callRequest.Parameters, SerializerSettings)})}});");
            }

            executeCode.Append("return out;");

            _executeLogger?.Debug(
                $"Вызов Execute метода с {callRequests.Count} подзапросами и {callRequests.Sum(x => x.ExecuteWeight)} весом запросов. \n[{string.Join(",\n", callRequests.Select(x => $"{{methodName = {x.Name}, weight = {x.ExecuteWeight}, addTime = {x.AddTime}, parameters = {string.Join(",", x.Parameters.Select(x => $"{x.Key}={x.Value}"))}}}"))}]");

            try
            {
                var rawRes = await Execute.ExecuteAsync(executeCode.ToString());
                cancellationToken.ThrowIfCancellationRequested();
                var res = rawRes.ToListOf(x => x["res"]);
                for (var i = 0; i < callRequests.Count; i++) callRequests[i].Task.SetResult(res[i]);
                _executeLogger?.Debug(
                    $"Execute метод успешно выполнен с {callRequests.Count} подзапросами и {callRequests.Sum(x => x.ExecuteWeight)} весом запросов. \n[{string.Join(",\n", callRequests.Select(x => $"{{methodName = {x.Name}, weight = {x.ExecuteWeight}, addTime = {x.AddTime}, parameters = {string.Join(",", x.Parameters.Select(x => $"{x.Key}={x.Value}"))}}}"))}]");
            }
            catch (TooManyRequestsException)
            {
                foreach (var methodData in callRequests)
                {
                    _callRequests.Enqueue(methodData);
                    Interlocked.Add(ref _currentRequestsWeight, methodData.ExecuteWeight);
                }
            }
            catch (ExecuteException e)
            {
                var rawResponse = JArray.Parse(e.Response.Value.ToString());
                var clearResponse = rawResponse.Select(x => x.SelectToken("res")).ToArray();
                var errId = 0;
                for (var i = 0; i < clearResponse.Length; i++)
                    if (clearResponse[i].Type == JTokenType.Boolean)
                        callRequests[i].Task.SetException(e.InnerExceptions[errId++]);
                    else
                        callRequests[i].Task.SetResult(new VkResponse(clearResponse[i]));
            }
            catch (ErrorExecutingCodeException)
            {
                _executeLogger?.Warn(
                    $"Размер ответа слишком большой. Всего подзапросов {callRequests.Count}, вес подзапросов {callRequests.Sum(x => x.ExecuteWeight)}");
                _lastAddTime = DateTime.Now;
                foreach (var methodData in callRequests)
                {
                    if (methodData.ExecuteWeight < MaxExecute) methodData.ExecuteWeight += 1;
                    _callRequests.Enqueue(methodData);
                    Interlocked.Add(ref _currentRequestsWeight, methodData.ExecuteWeight);
                    _executeLogger?.Debug(
                        $"Новый вес заспроса {methodData.Name} = {methodData.ExecuteWeight}, параметры {string.Join(",", methodData.Parameters.Where(x => x.Key != Constants.AccessToken).Select(x => $"{x.Key}={x.Value}"))} {methodData.ExecuteWeight}");
                }
            }
            catch (OperationCanceledException)
            {
                foreach (var callRequest in callRequests) callRequest.Task.SetCanceled(cancellationToken);
            }
            catch (CaptchaNeededException e)
            {
                foreach (var callRequest in callRequests) callRequest.Task.SetException(e);
            }
        }
    }
}
