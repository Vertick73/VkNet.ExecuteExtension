using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Serilog;
using Serilog.Events;
using VkNet.Model;

namespace VkNet.ExecuteExtension.Tests
{
    [TestFixture]
    public abstract class TestBase
    {
        [OneTimeSetUp]
        public void Init()
        {
            Directory.CreateDirectory("logs");
            var path = Path.Combine("logs",
                $"{TestContext.CurrentContext.Test.ClassName}[{DateTime.Now:yyyy-dd-M--HH-mm-ss}].log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("VkNet.ExecuteExtension", LogEventLevel.Verbose)
                .WriteTo.Console()
                .WriteTo.File(path)
                .CreateLogger();

            var serviceCollection = new ServiceCollection()
                .AddLogging(loggingBuilder =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                    loggingBuilder.AddSerilog(dispose: true);
                });

            vk = new VkApiExecute(serviceCollection);
            vk.Authorize(new ApiAuthParams { AccessToken = File.ReadAllText("Tokens.txt") });
        }

        [OneTimeTearDown]
        public void End()
        {
            Log.CloseAndFlush();
            vk.Dispose();
        }

        [SetUp]
        public void Start()
        {
            Log.Information("Run {TestName}", TestContext.CurrentContext.Test.FullName);
        }

        [TearDown]
        public void Finish()
        {
            Log.Information("Test {Status}| {TestName}", TestContext.CurrentContext.Result.Outcome.Status,
                TestContext.CurrentContext.Test.FullName);
        }

        protected VkApiExecute vk;
    }
}
