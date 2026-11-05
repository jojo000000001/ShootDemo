using System.IO;
using System.Linq;
using ShootDemo.Combat;
using ShootDemo.Config;
using ShootDemo.Core;
using ShootDemo.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShootDemo.Editor
{
    /// <summary>Creates the C1 assets from the imported art without editing the source prefabs.</summary>
    public static class CombatC1Setup
    {
        [MenuItem("Shoot Demo/Setup C1 Combat Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            Directory.CreateDirectory("Assets/Prefabs/Enemies");
            Directory.CreateDirectory("Assets/Prefabs/Effects");
            Directory.CreateDirectory("Assets/Materials");
            AssetDatabase.Refresh();
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Config/GameConfig.asset");
            var flash = GetMaterial("Assets/Materials/EnemyHitFlash.mat", "ShootDemo/SpriteHitFlash");
            var trailMaterial = GetMaterial("Assets/Materials/BulletTrail.mat", "Sprites/Default");
            var bulletGlow = GetMaterial("Assets/Materials/BulletGlow.mat", "ShootDemo/SpriteHitFlash");
            bulletGlow.SetFloat("_FlashAmount", 1f);
            bulletGlow.SetColor("_FlashColor", new Color(1f, 0.95f, 0.65f));
            EditorUtility.SetDirty(bulletGlow);

            BuildEnemy("bianyi_ganranzhe_sanjieduan", "jiangshi", config.enemy.meleeHp,
                new Vector2(1.25f, 1.3f), config, flash);
            BuildEnemy("outuzhe", "outuzhe", config.enemy.rangedHp, new Vector2(1.15f, 1.3f), config, flash);
            BuildEnemy("chushouguai", "chushouguai", config.enemy.meleeHp,
                new Vector2(1.4f, 1.15f), config, flash);

            BuildNumber();
            BuildMuzzle();
            BuildImpact();
            BuildBullet(config, trailMaterial, bulletGlow);

            var root = GameObject.Find("CombatEffects");
            if (root == null) root = new GameObject("CombatEffects");
            var hitStop = root.GetComponent<HitStopController>();
            if (hitStop == null) hitStop = root.AddComponent<HitStopController>();
            var effects = root.GetComponent<CombatEffects>();
            if (effects == null) effects = root.AddComponent<CombatEffects>();
            SetReference(effects, "config", config);
            SetReference(effects, "damageNumberPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Effects/DamageNumber.prefab").GetComponent<DamageNumber>());
            SetReference(effects, "impactPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Effects/BulletImpact.prefab").GetComponent<CombatVisualEffect>());
            SetReference(effects, "muzzlePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Effects/MuzzleFlash.prefab").GetComponent<CombatVisualEffect>());

            if (GameObject.Find("C1_TestTargets") == null)
            {
                var targets = new GameObject("C1_TestTargets");
                var names = new[] { "bianyi_ganranzhe_sanjieduan", "outuzhe", "chushouguai" };
                var desired = new[] { new Vector2(-4f, 3f), new Vector2(0f, 4.5f), new Vector2(4f, 3f) };
                for (int i = 0; i < names.Length; i++)
                {
                    Vector2 position = FindClearPosition(desired[i]);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/" + names[i] + ".prefab");
                    var target = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    target.transform.SetParent(targets.transform);
                    target.transform.position = position;
                    target.transform.rotation = Quaternion.Euler(0f, 0f, -90f);
                }
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Debug.Log("C1: three enemy prefabs, pooled effects and test targets are ready.");
        }

        private static Vector2 FindClearPosition(Vector2 desired)
        {
            for (int step = 0; step < 10; step++)
            {
                Vector2 candidate = desired + Vector2.up * (step * 0.6f);
                if (!Physics2D.OverlapCircleAll(candidate, 1.25f).Any(c => !c.isTrigger)) return candidate;
            }
            throw new System.InvalidOperationException("No clear test target location near " + desired);
        }

        private static Material GetMaterial(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) throw new System.InvalidOperationException("Missing shader " + shaderName);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        private static void BuildEnemy(string name, string animationPrefix, float hp, Vector2 size,
            GameConfig config, Material flash)
        {
            var root = new GameObject(name);
            try
            {
                var body = root.AddComponent<Rigidbody2D>();
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var collider = root.AddComponent<CapsuleCollider2D>();
                collider.size = size;
                collider.direction = CapsuleDirection2D.Horizontal;
                var art = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Arts/guaiwu/" + name + "/" + name + ".prefab"));
                art.name = "Visual";
                art.transform.SetParent(root.transform, false);
                art.transform.localPosition = Vector3.zero;
                var group = art.AddComponent<SortingGroup>();
                group.sortingOrder = 10000;
                foreach (var sprite in art.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (!sprite.name.Contains("yinying")) sprite.sharedMaterial = flash;
                }
                var animator = art.GetComponent<Animator>();
                animator.applyRootMotion = false;
                var health = root.AddComponent<EnemyHealth>();
                var data = new SerializedObject(health);
                data.FindProperty("maxHealth").floatValue = hp;
                data.FindProperty("deathState").stringValue = "Base Layer." + animationPrefix + "_dead";
                data.ApplyModifiedPropertiesWithoutUndo();
                var feedback = root.AddComponent<EnemyHitFeedback>();
                SetReference(feedback, "config", config);
                if (animationPrefix != "outuzhe") MeleeC2Setup.Configure(root, animationPrefix, config);
                CombatC3C4Setup.ConfigureEnemy(root, animationPrefix, config);
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Enemies/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildNumber()
        {
            var root = new GameObject("DamageNumber");
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                ConfigureLabel(root, font, new Color(1f, 0.85f, 0.35f), 31001);
                var shadow = new GameObject("Shadow");
                shadow.transform.SetParent(root.transform, false);
                shadow.transform.localPosition = new Vector3(0.025f, -0.025f, 0f);
                ConfigureLabel(shadow, font, new Color(0.08f, 0.05f, 0.02f), 31000);
                root.AddComponent<DamageNumber>();
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Effects/DamageNumber.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void ConfigureLabel(GameObject root, Font font, Color color, int order)
        {
            var label = root.AddComponent<TextMesh>();
            label.font = font;
            label.fontSize = 64;
            label.characterSize = 0.09f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontStyle = FontStyle.Bold;
            label.color = color;
            label.text = "10";
            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = order;
        }

        private static void BuildMuzzle()
        {
            var root = new GameObject("MuzzleFlash");
            try
            {
                var sprite = root.AddComponent<SpriteRenderer>();
                sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Arts/zhujue/weapon/weapon_huoqiang/weapon_huoqiang_huoguang.png");
                sprite.sortingOrder = 30001;
                sprite.color = new Color(1f, 0.85f, 0.5f);
                root.transform.localScale = Vector3.one * 0.65f;
                root.AddComponent<CombatVisualEffect>();
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Effects/MuzzleFlash.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildImpact()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Arts/zidan/huoqiangzidan/zidan_huoqiangzidan.prefab");
            var particle = source.GetComponentsInChildren<ParticleSystem>(true).First(p => p.name == "effect_suipian");
            var root = new GameObject("BulletImpact");
            try
            {
                var ps = Object.Instantiate(particle, root.transform);
                ps.gameObject.SetActive(true);
                ps.transform.localPosition = Vector3.zero;
                ps.transform.localRotation = Quaternion.identity;
                ps.transform.localScale = Vector3.one;
                var main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                main.duration = 0.1f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                main.startColor = new Color(1f, 0.8f, 0.3f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.02f;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.sortingOrder = 30000;
                root.AddComponent<CombatVisualEffect>();
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Effects/BulletImpact.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildBullet(GameConfig config, Material material, Material glow)
        {
            const string path = "Assets/Prefabs/PlayerBullet.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                SetReference(root.GetComponent<Bullet>(), "config", config);
                root.GetComponent<SpriteRenderer>().sortingOrder = 30000;
                root.GetComponent<SpriteRenderer>().sharedMaterial = glow;
                var trail = root.GetComponent<TrailRenderer>();
                if (trail == null) trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = material;
                trail.time = config.combatFeedback.trailLifetime;
                trail.startWidth = config.combatFeedback.trailWidth;
                trail.endWidth = 0f;
                trail.minVertexDistance = 0.025f;
                trail.emitting = false;
                trail.autodestruct = false;
                trail.sortingOrder = 29999;
                trail.startColor = new Color(1f, 0.95f, 0.6f, 0.9f);
                trail.endColor = new Color(1f, 0.3f, 0.02f, 0f);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetReference(Component component, string field, Object value)
        {
            var data = new SerializedObject(component);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
