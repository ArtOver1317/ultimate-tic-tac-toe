using System;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Runtime.Services.Scenes
{
    public class SceneLoaderService : ISceneLoaderService
    {

        public void LoadSceneAsync(string sceneName, Action onLoaded = null) => LoadSceneAsyncInternal(sceneName, onLoaded).Forget();

        private async UniTaskVoid LoadSceneAsyncInternal(string sceneName, Action onLoaded)
        {
            await SceneManager.LoadSceneAsync(sceneName).ToUniTask();
            onLoaded?.Invoke();
        }
    }
}

