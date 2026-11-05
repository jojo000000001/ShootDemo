using System.IO;
using System.Linq;
using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShootDemo.Editor
{
    public static class CombatC3C4Setup
    {
        [MenuItem("Shoot Demo/Setup C3 C4 Enemy Combat")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            Directory.CreateDirectory("Assets/Prefabs/Enemies");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Config/GameConfig.asset");
            BuildProjectile();
            BuildImpact();
            string[] names = { "bianyi_ganranzhe_sanjieduan", "chushouguai", "outuzhe" };
            string[] prefixes = { "jiangshi", "chushouguai", "outuzhe" };
            for (int i = 0; i < names.Length; i++)
            {
                string path = "Assets/Prefabs/Enemies/" + names[i] + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureEnemy(root, prefixes[i], config);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var service = GameObject.Find("EnemyCombat");
            if (service == null) service = new GameObject("EnemyCombat");
            if (service.GetComponent<EnemyRegistry>() == null) service.AddComponent<EnemyRegistry>();
            AIC8Setup.Configure(service, config);
            var shots = service.GetComponent<EnemyProjectiles>();
            if (shots == null) shots = service.AddComponent<EnemyProjectiles>();
            SetObject(shots, "config", config);
            SetObject(shots, "projectilePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Enemies/OutuzheProjectile.prefab").GetComponent<EnemyProjectile>());
            SetObject(shots, "impactPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Effects/OutuzheImpact.prefab").GetComponent<CombatVisualEffect>());
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(service.scene);
            EditorSceneManager.SaveScene(service.scene);
        }

        public static void ConfigureEnemy(GameObject root, string prefix, GameConfig config)
        {
            if (prefix == "outuzhe")
            {
                var ai = root.GetComponent<RangedEnemy>();
                if (ai == null) ai = root.AddComponent<RangedEnemy>();
                SetObject(ai, "config", config);
                var animator = root.GetComponentInChildren<Animator>();
                var visibility = root.GetComponent<EnemyAttackVisibility>();
                if (visibility == null) visibility = root.AddComponent<EnemyAttackVisibility>();
                SetObject(visibility, "bodyRenderer", animator.GetComponentsInChildren<SpriteRenderer>(true)
                    .First(sprite => sprite.name == "outuzhe_body"));
                if (animator.GetComponent<EnemyAnimationEvents>() == null) animator.gameObject.AddComponent<EnemyAnimationEvents>();
                SetObject(ai, "muzzle", animator.GetComponentsInChildren<Transform>(true).First(t => t.name == "outuzhe_mouth_3"));
                var line = root.transform.Find("AttackWarning");
                if (line == null)
                {
                    line = new GameObject("AttackWarning").transform;
                    line.SetParent(root.transform, false);
                }
                var warning = line.GetComponent<LineRenderer>();
                if (warning == null) warning = line.gameObject.AddComponent<LineRenderer>();
                warning.useWorldSpace = true;
                warning.positionCount = 2;
                warning.startWidth = 0.075f;
                warning.endWidth = 0.035f;
                warning.startColor = new Color(1f, 0.25f, 0.05f, 0.85f);
                warning.endColor = new Color(1f, 0.55f, 0.1f, 0.25f);
                warning.numCapVertices = 4;
                warning.sortingOrder = 25000;
                warning.shadowCastingMode = ShadowCastingMode.Off;
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EnemyWarning.mat");
                if (material == null)
                {
                    material = new Material(Shader.Find("Sprites/Default"));
                    AssetDatabase.CreateAsset(material, "Assets/Materials/EnemyWarning.mat");
                }
                warning.sharedMaterial = material;
                warning.enabled = false;
                SetObject(ai, "warning", warning);
            }
            var lifecycle = root.GetComponent<EnemyLifecycle>();
            if (lifecycle == null) lifecycle = root.AddComponent<EnemyLifecycle>();
            SetObject(lifecycle, "config", config);
            CrowdC6C7Setup.Configure(root, config);
        }

        private static void BuildProjectile()
        {
            var root = new GameObject("OutuzheProjectile");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Arts/guaiwu/outuzhe/zidan_outuzhe/zidan_outuzhe.prefab");
                var art = (GameObject)PrefabUtility.InstantiatePrefab(source);
                art.name = "Visual";
                art.transform.SetParent(root.transform, false);
                foreach (var transform in art.GetComponentsInChildren<Transform>(true))
                {
                    Vector3 position = transform.localPosition;
                    position.z = 0f;
                    transform.localPosition = position;
                }
                art.AddComponent<SortingGroup>().sortingOrder = 26000;
                foreach (var trail in art.GetComponentsInChildren<TrailRenderer>(true)) trail.emitting = false;
                var animator = art.GetComponent<Animator>();
                animator.applyRootMotion = false;
                root.AddComponent<EnemyProjectile>();
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Enemies/OutuzheProjectile.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildImpact()
        {
            var root = new GameObject("OutuzheImpact");
            try
            {
                var sprite = root.AddComponent<SpriteRenderer>();
                sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Arts/guaiwu/outuzhe/zidan_outuzhe/image/zidan_outuzhe_jianshe.png");
                sprite.sortingOrder = 26001;
                root.AddComponent<CombatVisualEffect>();
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Effects/OutuzheImpact.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void SetObject(Component component, string field, Object value)
        {
            var data = new SerializedObject(component);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
