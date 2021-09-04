using System.Collections.Generic;
using VkNet.Abstractions;

namespace VkNet.ExecuteExtension
{
    public class VkApiClientWithListing : VkApiClient<MethodData>
    {
        public VkApiClientWithListing(IVkApi vkApi) : base(vkApi)
        {
        }

        public VkApiClientWithListing(string token) : base(token)
        {
        }

        public FilterType FilteType { get; set; } = FilterType.None;
        public List<string> FilterValues { get; } = new();
    }
}