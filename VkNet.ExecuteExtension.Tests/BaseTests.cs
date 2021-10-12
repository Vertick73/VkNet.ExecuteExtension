using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    public class BaseTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            var config = new LoggingConfiguration();
            var logfile = new FileTarget("logfile") { FileName = "TestLogs.txt", DeleteOldFileOnStartup = true };
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logfile);
            LogManager.Configuration = config;
            log = LogManager.GetCurrentClassLogger();

            vk = new VkApiExecute(log);
            vk.Authorize(new ApiAuthParams { AccessToken = File.ReadAllText("Tokens.txt") });
        }

        private Logger log;
        private VkApiExecute vk;

        [Test]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)50)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)30)]
        public async Task WallGetBigResponseTest(string domain, ulong count, ulong offset, ulong countLimit)
        {
            log.Debug(
                $"Run WallGetBigResponseTest: domain={domain}, count={count}, offset={offset}, countLimit={countLimit}");
            // Arrange
            var tasks = new Dictionary<Task<WallGetObject>, WallGetParams>();
            for (ulong i = 0; i < count; i += countLimit)
            {
                var parmsForTask = new WallGetParams
                {
                    Domain = domain,
                    Count = i + countLimit > count ? count - i : countLimit,
                    Offset = offset + i
                };
                var task = new Task<WallGetObject>(() => vk.Wall.Get(parmsForTask), TaskCreationOptions.LongRunning);
                tasks.Add(task, parmsForTask);
            }

            // Act
            foreach (var tasksKey in tasks.Keys) tasksKey.Start();
            await Task.WhenAll(tasks.Keys);

            // Assert
            foreach (var resTask in tasks) Assert.AreEqual(resTask.Key.Result.WallPosts.Count, resTask.Value.Count);
        }

        [Test]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 10)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 5)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 7)]
        public async Task WallGetBigResponseTestMaxExecute(string domain, ulong count, ulong offset, ulong countLimit,
            int executeLimit)
        {
            log.Debug(
                $"Run WallGetBigResponseTestMaxExecute: domain={domain}, count={count}, offset={offset}, countLimit={countLimit}, executeLimit={executeLimit}");
            vk.MaxExecute = executeLimit;
            await WallGetBigResponseTest(domain, count, offset, countLimit);
            vk.MaxExecute = 25;
        }

        [Test]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 4)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 5)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 6)]
        public async Task WallGetBigResponseTestMethodsWeight(string domain, ulong count, ulong offset,
            ulong countLimit, int methodWeight)
        {
            log.Debug(
                $"Run WallGetBigResponseTestMethodsWeight: domain={domain}, count={count}, offset={offset}, countLimit={countLimit}, methodWeight={methodWeight}");
            vk.MethodsWeight = new Dictionary<string, int> { { "wall.get", methodWeight } };
            await WallGetBigResponseTest(domain, count, offset, countLimit);
            vk.MethodsWeight = new Dictionary<string, int>();
        }

        [Test]
        public async Task FlushTest()
        {
            log.Debug("Run FlushTest");
            // Arrange
            vk.MaxWaitingTime = TimeSpan.FromSeconds(20);
            vk.PendingTime = TimeSpan.FromSeconds(20);

            long count = 10000;
            long countLimit = 1000;
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

            var stopwatch = new Stopwatch();


            // Act
            foreach (var tasksKey in tasks.Keys) tasksKey.Start();
            stopwatch.Start();
            vk.Flush();
            await Task.WhenAll(tasks.Keys);

            // Assert
            stopwatch.Stop();
            Assert.Less(stopwatch.Elapsed, TimeSpan.FromSeconds(10));
            foreach (var resTask in tasks) Assert.AreEqual(resTask.Key.Result.Count, resTask.Value.Count);

            vk.MaxWaitingTime = TimeSpan.FromSeconds(5);
            vk.PendingTime = TimeSpan.FromSeconds(1);
        }

        [Test]
        public async Task ExecuteSkipMethodsTest()
        {
            log.Debug("Run ExecuteSkipMethodsTest");

            // Arrange
            vk.MaxWaitingTime = TimeSpan.FromSeconds(20);
            vk.PendingTime = TimeSpan.FromSeconds(20);
            vk.SkipMethods = new HashSet<string> { "groups.getMembers" };
            var parmsForTask = new GroupsGetMembersParams
            {
                GroupId = "41152133",
                Count = 1000,
                Offset = 0
            };
            var task = new Task<VkCollection<User>>(() => vk.Groups.GetMembers(parmsForTask),
                TaskCreationOptions.LongRunning);
            var stopwatch = new Stopwatch();

            // Act
            task.Start();
            stopwatch.Start();
            await task;

            // Assert
            Assert.Less(stopwatch.Elapsed, TimeSpan.FromSeconds(10));
            Assert.AreEqual(task.Result.Count, parmsForTask.Count);

            vk.MaxWaitingTime = TimeSpan.FromSeconds(5);
            vk.PendingTime = TimeSpan.FromSeconds(1);
            vk.SkipMethods = new HashSet<string>();
        }
    }
}
