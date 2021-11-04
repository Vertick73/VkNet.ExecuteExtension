using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VkNet.Abstractions;
using VkNet.Model;
using VkNet.Model.RequestParams;

namespace VkNet.ExecuteExtension.RequestRateTest
{
    internal class Program
    {
        private static IVkApi _vkApiExec;

        private static async Task Main(string[] args)
        {
            _vkApiExec = new VkApiExecute();
            _vkApiExec.Authorize(new ApiAuthParams
            {
                AccessToken = "your token"
            });
            var benchmark =
                new Benchmark<GroupsGetMembersParams>(ParamsProducer, TestConsumer, 100, 100, TimeSpan.FromSeconds(30));
            await benchmark.RunerTask;
        }

        public static IEnumerable<GroupsGetMembersParams> ParamsProducer()
        {
            var offset = 0;
            while (true)
            {
                yield return new GroupsGetMembersParams
                {
                    Count = 1000,
                    Offset = offset,
                    GroupId = "reddit"
                };
                offset += 1000;
            }
        }

        public static async Task TestConsumer(GroupsGetMembersParams @params)
        {
            await _vkApiExec.Groups.GetMembersAsync(@params);
        }
    }
}
