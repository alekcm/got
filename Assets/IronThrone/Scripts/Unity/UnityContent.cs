using System;
using System.Collections.Generic;
using System.IO;
using IronThrone.Core.Content;
using UnityEngine;

namespace IronThrone.Unity
{
    /// <summary>Поиск папки контента в редакторе, билде и StreamingAssets.</summary>
    public static class UnityContent
    {
        private static ContentDb _cached;
        private static string _dir;

        /// <summary>Папка, из которой загружен контент (для MapGen и других подпапок).</summary>
        public static string ContentDirectory => _dir;

        public static ContentDb Load()
        {
            if (_cached != null) return _cached;

            var candidates = new List<string>
            {
                Path.Combine(Application.dataPath, "IronThrone/Resources/IronThroneData"),
                Path.Combine(Application.streamingAssetsPath, "IronThroneData"),
                Path.Combine(Application.dataPath, "StreamingAssets/IronThroneData"),
                Path.Combine(Directory.GetCurrentDirectory(), "Assets/IronThrone/Resources/IronThroneData"),
            };
            foreach (var dir in candidates)
            {
                if (!Directory.Exists(dir)) continue;
                _dir = dir;
                _cached = ContentLoader.LoadFromDirectory(dir);
                Debug.Log("Iron Throne: контент загружен из " + dir);
                return _cached;
            }
            throw new DirectoryNotFoundException(
                "Папка IronThroneData не найдена. Скопируйте Assets/IronThrone/Resources/IronThroneData в StreamingAssets/IronThroneData для билда.");
        }

        public static string SavePath => Path.Combine(Application.persistentDataPath, "ironthrone_save.json");
        public static string AutoSavePath => Path.Combine(Application.persistentDataPath, "ironthrone_autosave.json");
    }
}
