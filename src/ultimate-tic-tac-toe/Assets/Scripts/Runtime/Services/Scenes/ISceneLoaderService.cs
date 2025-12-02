using System;

namespace Runtime.Services.Scenes
{
    public interface ISceneLoaderService
    {
        void LoadSceneAsync(string sceneName, Action onLoaded = null);
    }
}


