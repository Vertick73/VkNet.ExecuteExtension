using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using VkNet.Enums.Filters;
using VkNet.Exception;
using VkNet.Model;
using VkNet.Model.RequestParams;

namespace VkNet.ExecuteExtension.Tests
{
    [TestFixture]
    [Order(2)]
    public class PackingTests : TestBase
    {
        [SetUp]
        public void ResetConfig()
        {
            vk.MaxWaitingTime = TimeSpan.FromSeconds(3);
            vk.PendingTime = TimeSpan.FromSeconds(1);
            vk.SkipMethods = new HashSet<string>();
            vk.DefaultMethodWeight = 1;
            vk.MethodsWeight = new Dictionary<string, int>();
            vk.MaxExecute = 25;
        }

        [Test]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)50)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)30)]
        public async Task BigResponseTest(string domain, ulong count, ulong offset, ulong countLimit)
        {
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
                var task = vk.Wall.GetAsync(parmsForTask);
                tasks.Add(task, parmsForTask);
            }

            // Act
            await Task.WhenAll(tasks.Keys);

            // Assert
            foreach (var resTask in tasks) Assert.AreEqual(resTask.Key.Result.WallPosts.Count, resTask.Value.Count);
        }

        [Test]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 10)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 7)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 5)]
        public async Task MaxExecuteLimitTest(string domain, ulong count, ulong offset, ulong countLimit,
            int maxExecute)
        {
            vk.MaxExecute = maxExecute;
            await BigResponseTest(domain, count, offset, countLimit);
        }

        [Test]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 4)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 5)]
        [TestCase("internetpasta", (ulong)2500, (ulong)0, (ulong)100, 6)]
        public async Task MethodsWeightTest(string domain, ulong count, ulong offset,
            ulong countLimit, int methodWeight)
        {
            vk.MethodsWeight = new Dictionary<string, int> { { "wall.get", methodWeight } };
            await BigResponseTest(domain, count, offset, countLimit);
        }

        [Test]
        public async Task RightAndWrongMethods()
        {
            var resCount = 10;
            var wrongTask1 = vk.Users.GetFollowersAsync(3, resCount, 0, ProfileFields.FirstName);

            var rightTask1 = vk.Users.GetFollowersAsync(1, resCount, 0, ProfileFields.All);

            var wrongTask2 = vk.Groups.GetMembersAsync(new GroupsGetMembersParams
            {
                GroupId = "2",
                Count = resCount
            });

            var rightTask2 = vk.Groups.GetMembersAsync(new GroupsGetMembersParams
            {
                GroupId = "spacex",
                Count = resCount
            });


            var allTasks = Task.WhenAll(wrongTask1, rightTask1, wrongTask2, rightTask2);

            try
            {
                await Task.WhenAll(allTasks);
            }
            catch (System.Exception)
            {
                Assert.AreEqual(rightTask1.IsCompletedSuccessfully,true);
                Assert.AreEqual(rightTask1.Result.Count, resCount);
                Assert.AreEqual(rightTask2.IsCompletedSuccessfully, true);
                Assert.AreEqual(rightTask2.Result.Count, resCount);

                Assert.IsNotNull(wrongTask1.Exception);
                Assert.IsNotNull(wrongTask2.Exception);
                Assert.AreEqual(wrongTask1.Exception.InnerException.GetType(), typeof(UserDeletedOrBannedException));
                Assert.AreEqual(wrongTask2.Exception.InnerException.GetType(),
                    typeof(CannotBlacklistYourselfException));
            }

            Assert.Pass();
        }
    }
}
