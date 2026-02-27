namespace Runtime.Infrastructure.Save
{
    public enum SaveWriteError
    {
        None = 0,
        BackendWriteFailed = 1,
        SerializationFailed = 2,
        IncompatiblePersistedData = 3,
        NotInitialized = 4,
    }

    public readonly struct SaveWriteResult
    {
        public bool IsSuccess { get; }
        public SaveWriteError Error { get; }

        private SaveWriteResult(bool isSuccess, SaveWriteError error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public static SaveWriteResult Success() => new(true, SaveWriteError.None);

        public static SaveWriteResult Failed(SaveWriteError error) => new(false, error);
    }

    public interface ISaveServiceWithResult
    {
        SaveWriteResult TrySave<T>(string section, T data);
    }
}