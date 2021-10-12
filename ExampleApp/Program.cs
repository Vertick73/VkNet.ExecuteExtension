using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets;
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
            var config = new LoggingConfiguration();
            var logconsole = new ConsoleTarget("logconsole");
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logconsole);
            LogManager.Configuration = config;
            var log = LogManager.GetCurrentClassLogger();

            var vk = new VkApiExecute(log);
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
            {
                var parmsForTask = new WallGetParams
                {
                    Domain = "internetpasta",
                    Count = 100,
                    Offset = i
                };
                var task = Task.Factory.StartNew(() => vk.Wall.Get(parmsForTask), TaskCreationOptions.LongRunning);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine(
                $"Данные успешно получены. Всего постов получено: {tasks.Sum(x => x.Result.WallPosts.Count)}");
            Console.WriteLine($"Время выполнения всех запросов: {stopwatch.ElapsedMilliseconds} мс");
            Console.ResetColor();

            stopwatch.Reset();
            stopwatch.Start();
            //При меньшем объёме выходных данных можно упаковывать запросы более плотно
            vk.MaxExecute = 20;
            vk.MethodsWeight = new Dictionary<string, int>(); //отчистка особых начальных весов
            var tasks2 = new List<Task<WallGetObject>>();
            for (ulong i = 0; i < 3500; i += 100)
            {
                var parmsForTask = new WallGetParams
                {
                    Domain = "reddit",
                    Count = 100,
                    Offset = i
                };
                var task = Task.Factory.StartNew(() => vk.Wall.Get(parmsForTask), TaskCreationOptions.LongRunning);
                tasks2.Add(task);
            }

            await Task.WhenAll(tasks2);
            stopwatch.Stop();
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine(
                $"Данные успешно получены. Всего постов получено: {tasks2.Sum(x => x.Result.WallPosts.Count)}");
            Console.WriteLine($"Время выполнения всех запросов: {stopwatch.ElapsedMilliseconds} мс");
            Console.ResetColor();
        }
    }
}
