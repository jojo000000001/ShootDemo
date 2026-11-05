using ShootDemo.Config;
using ShootDemo.Enemy;
using ShootDemo.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShootDemo.Editor
{
    public static class SpawnerC5Setup
    {
        [MenuItem("Shoot Demo/Setup C5 Enemy Spawner")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            var root = GameObject.Find("EnemyCombat");
            if (root == null || root.GetComponent<EnemyRegistry>() == null)
                throw new System.InvalidOperationException("Set up C3 C4 enemy combat first.");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Config/GameConfig.asset");
            var spawner = root.GetComponent<EnemySpawner>();
            bool firstSetup = spawner == null;
            if (firstSetup) spawner = root.AddComponent<EnemySpawner>();
            var data = new SerializedObject(spawner);
            data.FindProperty("config").objectReferenceValue = config;
            data.FindProperty("registry").objectReferenceValue = root.GetComponent<EnemyRegistry>();
            data.FindProperty("player").objectReferenceValue = Object.FindObjectOfType<PlayerHealth>();
            var entries = data.FindProperty("enemies");
            entries.arraySize = 3;
            string[] names = { "bianyi_ganranzhe_sanjieduan", "chushouguai", "outuzhe" };
            for (int i = 0; i < names.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/Enemies/" + names[i] + ".prefab").GetComponent<EnemyLifecycle>();
                entry.FindPropertyRelative("weight").floatValue = i == 2 ? 1f : 2f;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            if (firstSetup)
            {
                config.spawn.waveCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
                EditorUtility.SetDirty(config);
            }
            var targets = GameObject.Find("C1_TestTargets");
            if (targets != null) targets.SetActive(false);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
        }
    }
}
