namespace Runtime.Infrastructure.Save
{
    internal interface ISaveBackend
    {
        string Read();
        void Write(string data);
        string GetDisplayPath();
    }
}