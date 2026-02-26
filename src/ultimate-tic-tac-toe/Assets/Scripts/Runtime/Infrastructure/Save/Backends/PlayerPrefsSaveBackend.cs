using UnityEngine;

namespace Runtime.Infrastructure.Save.Backends
{
    internal sealed class PlayerPrefsSaveBackend : ISaveBackend
    {
        private const string SaveKey = "SaveSystem.Data";

        public string Read()
            => PlayerPrefs.GetString(SaveKey, string.Empty);

        public void Write(string data)
        {
            PlayerPrefs.SetString(SaveKey, data);
            PlayerPrefs.Save();
        }

        public string GetDisplayPath() => SaveKey;
    }
}