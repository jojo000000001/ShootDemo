using ShootDemo.Config;
using ShootDemo.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShootDemo.Editor
{
    public static class AIC8Setup
    {
        [MenuItem("Shoot Demo/Setup C8 AI Scheduling")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Config/GameConfig.asset");
            var registry = Object.FindObjectOfType<EnemyRegistry>();
            if (registry == null) throw new System.InvalidOperationException("Set up EnemyCombat first.");
            Configure(registry.gameObject, config);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);
            EditorSceneManager.SaveScene(registry.gameObject.scene);
        }

        public static void Configure(GameObject service, GameConfig config)
        {
            var registry = service.GetComponent<EnemyRegistry>();
            if (registry == null) registry = service.AddComponent<EnemyRegistry>();
            var scheduler = service.GetComponent<EnemyAIScheduler>();
            if (scheduler == null) scheduler = service.AddComponent<EnemyAIScheduler>();
            foreach (Component component in new Component[] { registry, scheduler })
            {
                var data = new SerializedObject(component);
                data.FindProperty("config").objectReferenceValue = config;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
