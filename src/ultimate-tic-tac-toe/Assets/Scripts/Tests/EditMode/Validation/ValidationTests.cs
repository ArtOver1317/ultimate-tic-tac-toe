using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tests.EditMode.Validation
{
    [TestFixture]
    [Category("Integration")]
    public class ValidationTests
    {
        [TestCaseSource(nameof(AllScenesPaths))]
        public void WhenSceneLoaded_ThenGameObjectsHaveNoMissingScripts(string scenePath)
        {
            var openedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var gameObjectsWithMissingScripts = new List<string>();

            try
            {
                foreach (var gameObject in GetAllGameObjects(openedScene))
                {
                    if (!HasMissingComponents(gameObject))
                        continue;

                    var path = BuildHierarchyPath(gameObject.transform);
                    var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    gameObjectsWithMissingScripts.Add($"{scenePath} | {path} | missing={count}");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(openedScene, true);
            }

            gameObjectsWithMissingScripts.Should().BeEmpty();
        }

        [TestCaseSource(nameof(AllPrefabPaths))]
        public void WhenPrefabLoaded_ThenGameObjectsHaveNoMissingScripts(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            prefab.Should().NotBeNull($"Failed to load prefab at '{prefabPath}'");
            
            var gameObjectsWithMissingScripts = new List<string>();
            
            foreach (var gameObject in GetAllGameObjects(prefab))
            {
                if (!HasMissingComponents(gameObject))
                    continue;

                var path = BuildHierarchyPath(gameObject.transform);
                var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                gameObjectsWithMissingScripts.Add($"{prefabPath} | {path} | missing={count}");
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

        private static string BuildHierarchyPath(Transform transform)
        {
            var stack = new Stack<string>();
            var current = transform;

            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack);
        }
    }
}
