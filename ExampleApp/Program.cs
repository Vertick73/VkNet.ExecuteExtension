using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using VkNet.ExecuteExtension;
using VkNet.Model;
using VkNet.Model.RequestParams;

namespace ExampleApp
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            //Логирование в консоль для отладки
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("VkNet.ExecuteExtension", LogEventLevel.Verbose)
                .WriteTo.Console()
                .CreateLogger();

            var serviceCollection = new ServiceCollection()
                .AddLogging(loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                    loggingBuilder.AddSerilog(dispose: true);
                });

            var vk = new VkApiExecute(serviceCollection);
            vk.Authorize(new ApiAuthParams
            {
                AccessToken = "your token"
            });
            vk.MaxExecute = 15; //Максимальный суммарный вес методов при вызове Execute. <=25
            vk.MethodsWeight = new Dictionary<string, int> { { "wall.get", 3 } }; // Особоые начальные веса для методов

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            //Пример получения большого количества постов со стены, в которых содержится много данных.
            // => за один вызов execute можно вытащить ~500 постов
            var tasks = new List<Task<WallGetObject>>();
            for (ulong i = 0; i < 2000; i += 100)
                tasks.Add(vk.Wall.GetAsync(new WallGetParams
                {
                    Domain = "internetpasta",
                    Count = 100,
                    Offset = i
                }));

            await Task.WhenAll(tasks);
            stopwatch.Stop();
            Log.Information(
                $"Данные успешно получены. Всего постов получено: {tasks.Sum(x => x.Result.WallPosts.Count)}");
            Log.Information($"Время выполнения всех запросов: {stopwatch.ElapsedMilliseconds} мс");

            stopwatch.Reset();
            stopwatch.Start();
            //При меньшем объёме выходных данных можно упаковывать запросы более плотно
            vk.MaxExecute = 20;
            vk.MethodsWeight = new Dictionary<string, int>(); //отчистка особых начальных весов
            var tasks2 = new List<Task<WallGetObject>>();
            for (ulong i = 0; i < 3300; i += 100)
                tasks2.Add(vk.Wall.GetAsync(new WallGetParams
                {
                    Domain = "reddit",
                    Count = 100,
                    Offset = i
                }));
            await Task.WhenAll(tasks2);
            stopwatch.Stop();
            Log.Information(
                $"Данные успешно получены. Всего постов получено: {tasks2.Sum(x => x.Result.WallPosts.Count)}");
            Log.Information($"Время выполнения всех запросов: {stopwatch.ElapsedMilliseconds} мс");
        }
    }
}
