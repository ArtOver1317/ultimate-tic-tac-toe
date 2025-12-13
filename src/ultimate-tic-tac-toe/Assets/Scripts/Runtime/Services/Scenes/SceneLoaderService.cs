using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Runtime.Services.Scenes
{
    public class SceneLoaderService : ISceneLoaderService
    {
        public async UniTask LoadSceneAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            await LoadSceneAsync(sceneName, LoadSceneMode.Single, cancellationToken);
        }

        public async UniTask LoadSceneAsync(string sceneName, LoadSceneMode mode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asyncOperation = SceneManager.LoadSceneAsync(sceneName, mode);
            await asyncOperation.ToUniTask(cancellationToken: cancellationToken);
        }
    }
}

