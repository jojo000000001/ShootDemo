using ShootDemo.Config;
using ShootDemo.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShootDemo.Editor
{
    public static class CrowdC6C7Setup
    {
        [MenuItem("Shoot Demo/Setup C6 C7 Enemy Crowd")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            EnsureEnemyLayer();
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Config/GameConfig.asset");
            foreach (string name in new[] { "bianyi_ganranzhe_sanjieduan", "chushouguai", "outuzhe" })
            {
                string path = "Assets/Prefabs/Enemies/" + name + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Configure(root, config);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var registry = Object.FindObjectOfType<EnemyRegistry>();
            AIC8Setup.Configure(registry.gameObject, config);
            var data = new SerializedObject(registry);
            data.FindProperty("config").objectReferenceValue = config;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);
            EditorSceneManager.SaveScene(registry.gameObject.scene);
        }

        public static void Configure(GameObject root, GameConfig config)
        {
            int layer = EnsureEnemyLayer();
            foreach (var collider in root.GetComponentsInChildren<Collider2D>(true)) collider.gameObject.layer = layer;
            var movement = root.GetComponent<EnemyCrowdMovement>();
            if (movement == null) movement = root.AddComponent<EnemyCrowdMovement>();
            var data = new SerializedObject(movement);
            data.FindProperty("config").objectReferenceValue = config;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int EnsureEnemyLayer()
        {
            int layer = LayerMask.NameToLayer("Enemy");
            if (layer < 0)
            {
                var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var layers = tags.FindProperty("layers");
                for (int i = 8; i < 32; i++)
                {
                    if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) continue;
                    layers.GetArrayElementAtIndex(i).stringValue = "Enemy";
                    tags.ApplyModifiedPropertiesWithoutUndo();
                    layer = i;
                    break;
                }
            }
            if (layer < 0) throw new System.InvalidOperationException("No user layer available for Enemy.");
            Physics2D.IgnoreLayerCollision(layer, layer, true);
            return layer;
        }
    }
}
