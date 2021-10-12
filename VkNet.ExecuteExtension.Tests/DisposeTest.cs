using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using VkNet.Model;
using VkNet.Model.RequestParams;
using VkNet.Utils;

namespace VkNet.ExecuteExtension.Tests
{
    [TestFixture]
    public class DisposeTest
    {
        [OneTimeSetUp]
        public void Setup()
        {
            var config = new LoggingConfiguration();
            var logfile = new FileTarget("logfile2")
                { FileName = "ExceptionTestsLogs.txt" };
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logfile);
            LogManager.Configuration = config;
            log = LogManager.GetCurrentClassLogger();

            vk = new VkApiExecute(log);
            vk.PendingTime = TimeSpan.FromSeconds(3);
            vk.Authorize(new ApiAuthParams { AccessToken = File.ReadAllText("Tokens.txt") });
        }

        private Logger log;
        private VkApiExecute vk;

        [Test]
        public async Task SimpleDisposeTest()
        {
            log.Debug("Run SimpleDisposeTest");
            // Arange
            long count = 3500;
            long countLimit = 100;
            long offset = 0;
            var tasks = new Dictionary<Task<VkCollection<User>>, GroupsGetMembersParams>();
            for (long i = 0; i < count; i += countLimit)
            {
                var parmsForTask = new GroupsGetMembersParams
                {
                    GroupId = "41152133",
                    Count = i + countLimit > count ? count - i : countLimit,
                    Offset = offset + i
                };
                var task = new Task<VkCollection<User>>(() => vk.Groups.GetMembers(parmsForTask),
                    TaskCreationOptions.LongRunning);
                tasks.Add(task, parmsForTask);
            }

            // Act
            foreach (var tasksKey in tasks.Keys) tasksKey.Start();
            await Task.Delay(500);
            vk.Dispose();
            try
            {
                await Task.WhenAll(tasks.Keys);
            }
            catch (TaskCanceledException)
            {
                log.Debug($"Cancaled tasks: {tasks.Keys.Count(x => x.IsFaulted)}");
            }

            log.Debug($"CompletedSuccessfully tasks: {tasks.Keys.Count(x => x.IsCompletedSuccessfully)}");
            // Assert
            foreach (var task in tasks)
            {
                if (task.Key.IsCompletedSuccessfully)
                {
                    Assert.AreEqual(task.Key.Result.Count, task.Value.Count);
                    continue;
                }

                if (task.Key.IsFaulted && task.Key.Exception.InnerExceptions.First() is TaskCanceledException) continue;
                Assert.Fail();
            }
        }
    }
}
