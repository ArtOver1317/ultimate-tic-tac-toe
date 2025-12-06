using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FluentAssertions;

namespace Tests.EditMode
{
    public class ValidationTests
    {
        [TestCaseSource(nameof(AllScenesPaths))]
        public void AllGameObjectsShouldNotHaveMissingScriptsInScenes(string scenePath)
        {
            var openedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var gameObjectsWithMissingScripts = new List<string>();
            
            foreach (var gameObject in GetAllGameObjects(openedScene))
            {
                if (HasMissingComponents(gameObject)) 
                    gameObjectsWithMissingScripts.Add(gameObject.name);
            }
            
            EditorSceneManager.CloseScene(openedScene, true);
            
            gameObjectsWithMissingScripts.Should().BeEmpty();
        }

        [TestCaseSource(nameof(AllPrefabPaths))]
        public void AllGameObjectsShouldNotHaveMissingScriptsInPrefabs(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            prefab.Should().NotBeNull($"Failed to load prefab at '{prefabPath}'");
            
            var gameObjectsWithMissingScripts = new List<string>();
            
            foreach (var gameObject in GetAllGameObjects(prefab))
            {
                if (HasMissingComponents(gameObject))
                    gameObjectsWithMissingScripts.Add(gameObject.name);
            }
            
            gameObjectsWithMissingScripts.Should().BeEmpty(prefab.name);
        }
        
        private static bool HasMissingComponents(GameObject gameObject) => 
            GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) > 0;

        private static IEnumerable<string> AllScenesPaths() =>
            AssetDatabase
                .FindAssets("t:Scene", new[] { "Assets/Scenes" })
                .Select(AssetDatabase.GUIDToAssetPath);

        private static IEnumerable<string> AllPrefabPaths() =>
            AssetDatabase
                .FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath);

        private static IEnumerable<GameObject> GetAllGameObjects(Scene scene)
        {
            var gameObjectsQueue = new Queue<GameObject>(scene.GetRootGameObjects());

            while (gameObjectsQueue.Count > 0)
            {
                var gameObject = gameObjectsQueue.Dequeue();
                yield return gameObject;

                foreach (Transform child in gameObject.transform)
                {
                    gameObjectsQueue.Enqueue(child.gameObject);
                }
            }
        }

        private static IEnumerable<GameObject> GetAllGameObjects(GameObject rootGameObject)
        {
            var gameObjectsQueue = new Queue<GameObject>();
            gameObjectsQueue.Enqueue(rootGameObject);

            while (gameObjectsQueue.Count > 0)
            {
                var gameObject = gameObjectsQueue.Dequeue();
                yield return gameObject;

                foreach (Transform child in gameObject.transform)
                {
                    gameObjectsQueue.Enqueue(child.gameObject);
                }
            }
        }
    }
}
