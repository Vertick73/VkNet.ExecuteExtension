using System;
using System.Threading.Tasks;
using VkNet.Utils;

namespace VkNet.ExecuteExtension
{
    public class CallRequest
    {
        public string Name { get; set; }
        public VkParameters Parameters { get; set; }
        public TaskCompletionSource<VkResponse> Task { get; set; }
        public int ExecuteIndex { get; set; }
        public int ExecuteWeight { get; set; }
        public DateTime AddTime { get; set; }
    }
}
