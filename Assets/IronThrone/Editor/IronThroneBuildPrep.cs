#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronThrone.EditorTools
{
    /// <summary>
    /// Настройка сцен и подготовка контента.
    /// Сцены проекта: <c>Assets/Scenes/MainMenu.unity</c> (меню) и <c>Assets/Scenes/Game.unity</c> (партия);
    /// они закоммичены в репозиторий и лежат в Build Settings. Этот скрипт лишь чинит проект,
    /// если сцены пропали, и копирует JSON-контент в StreamingAssets
    /// (в билде ContentLoader читает именно оттуда).
    /// </summary>
    public class IronThroneBuildPrep : IPreprocessBuildWithReport
    {
        private const string SourceDir = "Assets/IronThrone/Resources/IronThroneData";
        private const string TargetDir = "Assets/StreamingAssets/IronThroneData";
        private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameScenePath = "Assets/Scenes/Game.unity";
        private const string SoqMapScenePath = "Assets/Scenes/SoQMap.unity";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            CopyContent();
            EnsureScenes();
        }

        [MenuItem("Iron Throne/Настроить сцены (MainMenu, Game, SoQMap)")]
        public static void SetupScenes()
        {
            EnsureScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("Iron Throne: сцены готовы — " + MenuScenePath + ", " + GameScenePath + ", " + SoqMapScenePath + ". Нажмите Play в MainMenu.");
        }

        [MenuItem("Iron Throne/Подготовить StreamingAssets")]
        public static void CopyContentMenu()
        {
            CopyContent();
            Debug.Log("Iron Throne: контент скопирован в " + TargetDir);
        }

        /// <summary>Гарантирует, что обе сцены существуют и стоят первыми в Build Settings.</summary>
        private static void EnsureScenes()
        {
            EnsureScene(MenuScenePath);
            EnsureScene(GameScenePath);
            EnsureScene(SoqMapScenePath);
            RegisterScenes();
        }

        private static void EnsureScene(string path)
        {
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("Iron Throne: сцена пересоздана — " + path);
        }

        private static void RegisterScenes()
        {
            var others = EditorBuildSettings.scenes
                .Where(s => s.path != MenuScenePath && s.path != GameScenePath && s.path != SoqMapScenePath)
                .ToList();
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true),
                new EditorBuildSettingsScene(SoqMapScenePath, true),
            };
            list.AddRange(others);
            var current = EditorBuildSettings.scenes;
            if (current.Length == list.Count && current.Select(s => s.path).SequenceEqual(list.Select(s => s.path)))
                return;
            EditorBuildSettings.scenes = list.ToArray();
        }

        private static void CopyContent()
        {
            if (!Directory.Exists(SourceDir)) return;
            Directory.CreateDirectory(TargetDir);
            // копируем и подпапки (MapGen с layout/blueprint/каталогом объектов)
            foreach (var file in Directory.GetFiles(SourceDir, "*.json", SearchOption.AllDirectories))
            {
                var relative = file.Substring(SourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dst = Path.Combine(TargetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(file, dst, true);
            }
            AssetDatabase.Refresh();
        }
    }
}
#endif
