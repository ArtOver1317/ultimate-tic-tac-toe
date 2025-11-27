using System;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Services.Scenes
{
    public class SceneLoaderService : ISceneLoaderService
    {
        public void LoadScene(string sceneName, Action onLoaded = null)
        {
            SceneManager.LoadScene(sceneName);
            onLoaded?.Invoke();
        }

        public void LoadSceneAsync(string sceneName, Action onLoaded = null) => LoadSceneAsyncInternal(sceneName, onLoaded).Forget();

        private async UniTaskVoid LoadSceneAsyncInternal(string sceneName, Action onLoaded)
        {
            await SceneManager.LoadSceneAsync(sceneName).ToUniTask();
            onLoaded?.Invoke();
        }
    }
}

