using ShootDemo.Config;
using ShootDemo.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShootDemo.Editor
{
    public static class MeleeC2Setup
    {
        [MenuItem("Shoot Demo/Setup C2 Melee Enemies")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Config/GameConfig.asset");
            string[] names = { "bianyi_ganranzhe_sanjieduan", "chushouguai" };
            string[] prefixes = { "jiangshi", "chushouguai" };
            for (int i = 0; i < names.Length; i++)
            {
                string path = "Assets/Prefabs/Enemies/" + names[i] + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Configure(root, prefixes[i], config);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void Configure(GameObject root, string prefix, GameConfig config)
        {
            var melee = root.GetComponent<MeleeEnemy>();
            if (melee == null) melee = root.AddComponent<MeleeEnemy>();
            var data = new SerializedObject(melee);
            data.FindProperty("config").objectReferenceValue = config;
            data.FindProperty("animationPrefix").stringValue = prefix;
            var collider = root.GetComponent<CapsuleCollider2D>();
            data.FindProperty("navigationRadius").floatValue = Mathf.Max(collider.size.x, collider.size.y) * 0.5f + 0.06f;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
