using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Runtime.Services.Scenes
{
    public interface ISceneLoaderService
    {
        UniTask LoadSceneAsync(string sceneName, CancellationToken cancellationToken = default);
        UniTask LoadSceneAsync(string sceneName, LoadSceneMode mode, CancellationToken cancellationToken = default);
    }
}


