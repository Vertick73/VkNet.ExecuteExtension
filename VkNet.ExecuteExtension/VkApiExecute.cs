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
        private readonly ILogger<VkApiExecute> _executeLogger;
        private readonly List<Task> _executeRunTasks = new();
        private readonly ServiceProvider _executeServiceProvider;
        private readonly int _minThreads;
        private readonly bool _needOptimization;
        public readonly TimeSpan MaxOptimizationIdleTime = TimeSpan.FromSeconds(60);
        private int _currentRequestsWeight;
        private volatile bool _flush;
        private bool _isOptimized;
        private long _lastAddTimeTicks;
        private volatile int _maxExecute = 25;
        private IReadOnlyDictionary<string, int> _methodsWeight = new Dictionary<string, int>();
        private int _oldMinThreads;
        private IReadOnlySet<string> _skipMethods = new HashSet<string>();

        public VkApiExecute(ILogger<VkApi> logger, ILogger<VkApiExecute> executeLogger = null,
            ICaptchaSolver captchaSolver = null,
            IAuthorizationFlow authorizationFlow = null, bool optimizationThreadPoolMinThreads = true,
            int minThreads = 125)
            : base(logger, captchaSolver, authorizationFlow)
        {
            _needOptimization = optimizationThreadPoolMinThreads;
            _minThreads = minThreads;
            _cts = new CancellationTokenSource();
            _executeHandlerTask = ExecuteCycleTask(_cts.Token);
            _executeLogger = executeLogger;
        }

        public VkApiExecute(IServiceCollection serviceCollection = null, bool optimizationThreadPoolMinThreads = true,
            int minThreads = 125) : base(serviceCollection)
        {
            _needOptimization = optimizationThreadPoolMinThreads;
            _minThreads = minThreads;
            _cts = new CancellationTokenSource();
            _executeHandlerTask = ExecuteCycleTask(_cts.Token);

            if (serviceCollection != null)
            {
                serviceCollection.RegisterDefaultDependencies();
                _executeServiceProvider = serviceCollection.BuildServiceProvider();
                _executeLogger = _executeServiceProvider.GetService<ILogger<VkApiExecute>>();
            }
        }

        /// <summary>
        /// Методы, которые не нужно упаковывать
        /// </summary>
        public IReadOnlySet<string> SkipMethods
        {
            get => _skipMethods;
            set => _skipMethods = new HashSet<string>(value);
        }

        /// <summary>
        /// Дефолтный вес методов.
        /// </summary>
        public int DefaultMethodWeight { get; set; } = 1;

        /// <summary>
        /// Начальные веса для методов
        /// </summary>
        public IReadOnlyDictionary<string, int> MethodsWeight
        {
            get => _methodsWeight;
            set => _methodsWeight = new Dictionary<string, int>(value);
        }

        private DateTime LastAddTime
        {
            get => new(Interlocked.Read(ref _lastAddTimeTicks));
            set => Interlocked.Exchange(ref _lastAddTimeTicks, value.Ticks);
        }

        /// <summary>
        /// Максимальный суммарный вес методов при вызове Execute (<=25).
        /// </summary>
        public int MaxExecute
        {
            get => _maxExecute;
            set
            {
                if (value < 1 || value > 25)
                    throw new ArgumentException(@"Value must be positive and <=25", nameof(value));
                _maxExecute = value;
            }
        }

        /// <summary>
        /// Задержка проверки Execute
        /// </summary>
        public TimeSpan CheckDelay { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Максимальное время ожидания для запроса
        /// </summary>
        public TimeSpan MaxWaitingTime { get; set; } = TimeSpan.FromMilliseconds(5000);

        /// <summary>
        /// Время ожидания новых запросов
        /// </summary>
        public TimeSpan PendingTime { get; set; } = TimeSpan.FromMilliseconds(1000);

        public new VkResponse Call(string methodName, VkParameters parameters, bool skipAuthorization = false)
        {
            if (methodName == "execute") return base.Call(methodName, parameters, skipAuthorization);
            if (_skipMethods.Contains(methodName)) return base.Call(methodName, parameters, skipAuthorization);
            var weight = DefaultMethodWeight;
            if (_methodsWeight.TryGetValue(methodName, out var x)) weight = x;
            var tcs = new TaskCompletionSource<VkResponse>();
            var request = new CallRequest
            {
                Parameters = parameters, Name = methodName, Task = tcs, AddTime = DateTime.Now, ExecuteWeight = weight
            };
            _callRequests.Enqueue(request);
            Interlocked.Add(ref _currentRequestsWeight, request.ExecuteWeight);
            LastAddTime = DateTime.Now;
            return tcs.Task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public new void Dispose()
        {
            _cts.Cancel();
            _executeHandlerTask.Wait();
            if (_isOptimized) ThreadPoolOptimization(false);
            foreach (var callRequest in _callRequests) callRequest.Task.SetCanceled(_cts.Token);
            base.Dispose();
        }

        private List<CallRequest> GetRequests()
        {
            var requests = new List<CallRequest>();
            var remainingFreeWeight = _maxExecute;
            while (_callRequests.TryPeek(out var peak) && peak.ExecuteWeight <= remainingFreeWeight)
                if (_callRequests.TryDequeue(out var res))
                {
                    Interlocked.Add(ref _currentRequestsWeight, -res.ExecuteWeight);
                    requests.Add(res);
                    remainingFreeWeight -= res.ExecuteWeight;
                }

            return requests;
        }

        private async Task ExecuteCycleTask(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_currentRequestsWeight >= _maxExecute || IsTimeout())
                    {
                        if (!_isOptimized && _needOptimization) ThreadPoolOptimization(true);
                        var requestsToExecute = GetRequests();
                        _executeRunTasks.Add(ExecuteRun(requestsToExecute, cancellationToken));
                    }

                    await Task.Delay(CheckDelay, cancellationToken).ConfigureAwait(false);
                    _executeRunTasks.RemoveAll(x => x.IsCompletedSuccessfully);
                    if (_isOptimized && DateTime.Now - LastAddTime > MaxOptimizationIdleTime)
                        ThreadPoolOptimization(false);
                }
            }
            catch (OperationCanceledException)
            {
                await Task.WhenAll(_executeRunTasks);
            }
        }

        private bool IsTimeout()
        {
            if (_callRequests.IsEmpty) return false;
            if (_flush)
            {
                _flush = false;
                return true;
            }

            var minTime = DateTime.Now - MaxWaitingTime;
            return
                DateTime.Now - LastAddTime >
                PendingTime || _callRequests.Any(x => x.AddTime < minTime);
        }

        private void ThreadPoolOptimization(bool flag)
        {
            if (flag)
            {
                ThreadPool.GetMinThreads(out _oldMinThreads, out var completionPortThreads);
                ThreadPool.SetMinThreads(_minThreads, completionPortThreads);
                _isOptimized = true;
            }
            else
            {
                ThreadPool.GetMinThreads(out _, out var completionPortThreads);
                ThreadPool.SetMinThreads(_oldMinThreads, completionPortThreads);
                _isOptimized = false;
            }
        }

        public void Flush()
        {
            _flush = true;
        }

        private string GenerateExecuteCode(IList<CallRequest> callRequests)
        {
            var executeCode = new StringBuilder("var out = [];");
            var index = 0;
            foreach (var callRequest in callRequests)
            {
                callRequest.ExecuteIndex = index++;
                executeCode.Append(
                    $"out.push({{\"id\":{callRequest.ExecuteIndex}, \"res\":API.{callRequest.Name}({JsonConvert.SerializeObject(callRequest.Parameters, SerializerSettings)})}});");
            }

            executeCode.Append("return out;");
            return executeCode.ToString();
        }

        private async Task ExecuteRun(IList<CallRequest> callRequests, CancellationToken cancellationToken)
        {
            if (callRequests.Count == 0) return;
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var callRequest in callRequests)
                    callRequest.Task.SetCanceled(cancellationToken);
                return;
            }

            var code = GenerateExecuteCode(callRequests);
            _executeLogger?.LogTrace(
                $"Вызов Execute метода с {callRequests.Count} подзапросами и {callRequests.Sum(x => x.ExecuteWeight)} весом запросов. \n[{string.Join(",\n", callRequests.Select(x => $"{{methodName = {x.Name}, weight = {x.ExecuteWeight}, addTime = {x.AddTime}, parameters = {string.Join(",", x.Parameters.Select(x => $"{x.Key}={x.Value}"))}}}"))}]");

            try
            {
                var rawRes = await Task.Factory.StartNew(() => Execute.Execute(code),
                    TaskCreationOptions.LongRunning).ConfigureAwait(false);
                var res = rawRes.ToListOf(x => x["res"]);
                for (var i = 0; i < callRequests.Count; i++) callRequests[i].Task.SetResult(res[i]);
                _executeLogger?.LogTrace(
                    $"Execute метод успешно выполнен с {callRequests.Count} подзапросами и {callRequests.Sum(x => x.ExecuteWeight)} весом запросов. \n[{string.Join(",\n", callRequests.Select(x => $"{{methodName = {x.Name}, weight = {x.ExecuteWeight}, addTime = {x.AddTime}, parameters = {string.Join(",", x.Parameters.Select(x => $"{x.Key}={x.Value}"))}}}"))}]");
            }
            catch (TooManyRequestsException e)
            {
                _executeLogger?.LogTrace(
                    $"{e.Message}. Возврат {callRequests.Count} запросов с весом {callRequests.Sum(x => x.ExecuteWeight)} в очередь");
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
                _executeLogger?.LogWarning(
                    $"Размер ответа слишком большой. Всего подзапросов {callRequests.Count}, вес подзапросов {callRequests.Sum(x => x.ExecuteWeight)}");
                LastAddTime = DateTime.Now;
                foreach (var methodData in callRequests)
                {
                    if (methodData.ExecuteWeight < MaxExecute) methodData.ExecuteWeight += 1;
                    _callRequests.Enqueue(methodData);
                    Interlocked.Add(ref _currentRequestsWeight, methodData.ExecuteWeight);
                    _executeLogger?.LogWarning(
                        $"Новый вес заспроса {methodData.Name} = {methodData.ExecuteWeight}, параметры {string.Join(",", methodData.Parameters.Where(x => x.Key != Constants.AccessToken).Select(x => $"{x.Key}={x.Value}"))}");
                }
            }
            catch (System.Exception e)
            {
                foreach (var callRequest in callRequests) callRequest.Task.SetException(e);
            }
        }
    }
}
