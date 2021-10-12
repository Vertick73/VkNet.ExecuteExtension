using System;
using System.IO;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using VkNet.Enums.Filters;
using VkNet.Exception;
using VkNet.Model;
using VkNet.Model.RequestParams;

namespace VkNet.ExecuteExtension.Tests
{
    [TestFixture]
    [Order(2)]
    public class ExceptionTests
    {
        [OneTimeSetUp]
        public void Setup()
        {
            var config = new LoggingConfiguration();
            var logfile = new FileTarget("logfile2")
                { FileName = "ExceptionTestsLogs.txt", DeleteOldFileOnStartup = true };
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
        [Parallelizable(ParallelScope.Self)]
        public async Task WrongDataTestUserDeletedOrBannedException()
        {
            log.Debug("Run WrongDataTestUserDeletedOrBannedException");
            var task = Task.Factory.StartNew(() => vk.Users.GetFollowers(3, 10, 0, ProfileFields.All),
                TaskCreationOptions.LongRunning);

            try
            {
                await task;
            }
            catch (UserDeletedOrBannedException)
            {
                Assert.Pass();
            }

            Assert.Fail();
        }

        [Test]
        [Parallelizable(ParallelScope.Self)]
        public async Task WrongDataCannotBlacklistYourselfException()
        {
            log.Debug("Run WrongDataCannotBlacklistYourselfException");
            var task = Task.Factory.StartNew(() => vk.Groups.GetMembers(new GroupsGetMembersParams
            {
                GroupId = "2",
                Count = 10
            }), TaskCreationOptions.LongRunning);

            try
            {
                await task;
            }
            catch (CannotBlacklistYourselfException)
            {
                Assert.Pass();
            }

            Assert.Fail();
        }

        [Test]
        [Parallelizable(ParallelScope.Self)]
        public async Task CorrectData()
        {
            log.Debug("Run CorrectData");
            var task2 = Task.Factory.StartNew(() => vk.Users.GetFollowers(1, 10, 0, ProfileFields.All),
                TaskCreationOptions.LongRunning);

            try
            {
                await task2;
            }
            catch (System.Exception)
            {
                Assert.Fail();
            }

            Assert.Pass();
        }
    }
}
