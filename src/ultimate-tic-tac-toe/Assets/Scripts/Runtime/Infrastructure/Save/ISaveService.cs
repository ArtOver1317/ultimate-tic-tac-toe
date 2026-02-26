namespace Runtime.Infrastructure.Save
{
    public interface ISaveService
    {
        T Load<T>(string section, T defaultValue);
        void Save<T>(string section, T data);
    }
}