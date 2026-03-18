using System.IO;
using System.Text;
using UnityEngine;

namespace Runtime.Infrastructure.Save.Backends
{
    internal sealed class FileSaveBackend : ISaveBackend
    {
#if UNITY_EDITOR
        private const string _saveFileName = "save.editor.dat";
        private const string _tempFileName = "save.editor.tmp";
#else
        private const string _saveFileName = "save.dat";
        private const string _tempFileName = "save.tmp";
#endif

        private readonly string _saveFilePath = Path.Combine(Application.persistentDataPath, _saveFileName);
        private readonly string _tempFilePath = Path.Combine(Application.persistentDataPath, _tempFileName);

        public string Read() => 
            !File.Exists(_saveFilePath) ? string.Empty : File.ReadAllText(_saveFilePath, Encoding.UTF8);

        public void Write(string data)
        {
            var directoryPath = Path.GetDirectoryName(_saveFilePath);
            
            if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);

            File.WriteAllText(_tempFilePath, data, Encoding.UTF8);

            if (File.Exists(_saveFilePath))
                File.Replace(_tempFilePath, _saveFilePath, null);
            else
                File.Move(_tempFilePath, _saveFilePath);
        }

        public string GetDisplayPath() => _saveFilePath;
    }
}