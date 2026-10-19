#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronThrone.EditorTools
{
    /// <summary>
    /// Подготовка проекта: стартовая сцена и копирование JSON-контента в StreamingAssets
    /// (в билде ContentLoader читает именно оттуда).
    /// </summary>
    public class IronThroneBuildPrep : IPreprocessBuildWithReport
    {
        private const string SourceDir = "Assets/IronThrone/Resources/IronThroneData";
        private const string TargetDir = "Assets/StreamingAssets/IronThroneData";
        private const string ScenePath = "Assets/Scenes/Main.unity";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            CopyContent();
        }

        [MenuItem("Iron Throne/Создать стартовую сцену")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
                list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Iron Throne: сцена создана — " + ScenePath + ". Нажмите Play.");
        }

        [MenuItem("Iron Throne/Подготовить StreamingAssets")]
        public static void CopyContentMenu()
        {
            CopyContent();
            Debug.Log("Iron Throne: контент скопирован в " + TargetDir);
        }

        private static void CopyContent()
        {
            if (!Directory.Exists(SourceDir)) return;
            Directory.CreateDirectory(TargetDir);
            foreach (var file in Directory.GetFiles(SourceDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                var dst = Path.Combine(TargetDir, Path.GetFileName(file));
                File.Copy(file, dst, true);
            }
            AssetDatabase.Refresh();
        }
    }
}
#endif
