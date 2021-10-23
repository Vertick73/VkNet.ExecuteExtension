using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using VkNet.Model.RequestParams;

namespace VkNet.ExecuteExtension.Tests
{
    [TestFixture]
    [Order(1)]
    public class FeatureTests : TestBase
    {
        [OneTimeSetUp]
        public void InitConfig()
        {
            vk.MaxWaitingTime = TimeSpan.FromSeconds(20);
            vk.PendingTime = TimeSpan.FromSeconds(20);
        }

        [Test]
        [Order(1)]
        public async Task FlushTest()
        {
            // Arrange
            var count = 100;
            var parmsForTask = new GroupsGetMembersParams
            {
                GroupId = "41152133",
                Count = 100,
                Offset = 0
            };

            // Act
            var task = vk.Groups.GetMembersAsync(parmsForTask);
            while (vk.CurrentRequestsWeight == 0) await Task.Delay(10);

            vk.Flush();
            await Task.Delay(300);

            //Assert
            Assert.AreEqual(vk.CurrentRequestsWeight, 0);
            Assert.AreEqual(task.Result.Count, count);
        }

        [Test]
        [Order(2)]
        public async Task ExecuteSkipMethodsTest()
        {
            // Arrange
            vk.SkipMethods = new HashSet<string> { "groups.getMembers" };
            var parmsForTask = new GroupsGetMembersParams
            {
                GroupId = "41152133",
                Count = 1000,
                Offset = 0
            };

            // Act
            var task = vk.Groups.GetMembersAsync(parmsForTask);

            // Assert
            while (true)
            {
                Assert.AreEqual(vk.CurrentRequestsWeight, 0);
                if (task.IsCompletedSuccessfully)
                {
                    Assert.AreEqual(task.Result.Count, parmsForTask.Count);
                    Assert.Pass();
                }

                if (task.IsFaulted) Assert.Fail();

                await Task.Delay(10);
            }
        }

        //must be run last
        [Test]
        [Order(3)]
        public async Task DisposeTest()
        {
            // Arange
            var parmsForPackedTask = new GroupsGetMembersParams
            {
                GroupId = "41152133",
                Count = 100,
                Offset = 0
            };
            var parmsForUnpackedTask = new GroupsGetMembersParams
            {
                GroupId = "41152133",
                Count = 100,
                Offset = 100
            };


            // Act
            var packedTask = vk.Groups.GetMembersAsync(parmsForPackedTask);
            await Task.Delay(200);
            vk.Flush();

            await Task.Delay(200);
            var unPackedTask = vk.Groups.GetMembersAsync(parmsForUnpackedTask);

            await Task.Delay(100);
            vk.Dispose();

            // Assert
            try
            {
                await unPackedTask;
            }
            catch (TaskCanceledException)
            {
                await packedTask;
                Assert.Pass();
            }
            catch (ObjectDisposedException)
            {
                await packedTask;
                Assert.Pass();
            }

            Assert.Fail();
        }
    }
}
