namespace VkNet.ExecuteExtension
{
    public interface IVkApiContainer<T>
    {
        public T GetVkApi();
    }
}