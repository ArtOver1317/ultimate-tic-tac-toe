using UnityEngine;

namespace Runtime.Infrastructure.Save.Backends
{
    internal sealed class PlayerPrefsSaveBackend : ISaveBackend
    {
        private const string _saveKey = "SaveSystem.Data";

        public string Read()
            => PlayerPrefs.GetString(_saveKey, string.Empty);

        public void Write(string data)
        {
            PlayerPrefs.SetString(_saveKey, data);
            PlayerPrefs.Save();
        }

        public string GetDisplayPath() => _saveKey;
    }
}