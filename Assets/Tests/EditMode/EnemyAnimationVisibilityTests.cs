using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShootDemo.Core;
using ShootDemo.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShootDemo.Tests.EditMode
{
    public sealed class EnemyAnimationVisibilityTests
    {
        [TestCase("chushouguai", "chushouguai")]
        [TestCase("bianyi_ganranzhe_sanjieduan", "jiangshi")]
        [TestCase("outuzhe", "outuzhe")]
        public void CachedAttacksKeepTheirDurationAndRestartAfterReuse(string prefabName, string prefix)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Enemies/" + prefabName + ".prefab"));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor, scene);
            try
            {
                var animator = actor.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.CullCompletely;
                var assembly = typeof(MeleeEnemy).Assembly;
                var type = assembly.GetType("ShootDemo.Enemy.EnemyAnimationPlayer");
                var stateType = assembly.GetType("ShootDemo.Enemy.EnemyAnimation");
                var player = System.Activator.CreateInstance(type, animator, prefix);
                var play = type.GetMethod("Play");
                var reset = type.GetMethod("Reset");
                for (int reuse = 0; reuse < 3; reuse++)
                {
                    reset.Invoke(player, null);
                    foreach (var pair in new[] { new[] { "AttackPre", "attack_pre" }, new[] { "AttackDo", "attack_do" } })
                    {
                        object state = System.Enum.Parse(stateType, pair[0]);
                        float duration = 0.4f + reuse * 0.1f;
                        play.Invoke(player, new[] { state, (object)duration });
                        Assert.AreEqual(AnimatorCullingMode.CullCompletely, animator.cullingMode,
                            "Reading an offscreen attack duration must restore normal culling.");
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        animator.Update(0f);
                        var info = animator.GetCurrentAnimatorStateInfo(0);
                        Assert.AreEqual(Animator.StringToHash("Base Layer." + prefix + "_" + pair[1]), info.fullPathHash);
                        Assert.That(info.length, Is.EqualTo(duration).Within(0.0001f));
                        animator.Update(duration * 0.5f);
                        float progress = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                        play.Invoke(player, new[] { state, (object)duration });
                        animator.Update(0f);
                        Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(progress).Within(0.0001f),
                            "Repeating a state must not restart its animation.");
                        animator.cullingMode = AnimatorCullingMode.CullCompletely;
                    }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void DeathFadeDoesNotSurviveThreePoolReuses()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("VisibilityPool");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            root.SetActive(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/chushouguai.prefab")
                .GetComponent<EnemyHealth>();
            var pool = new Pool<EnemyHealth>(prefab, 0, root.transform);
            try
            {
                var actor = pool.Get();
                typeof(EnemyHealth).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(actor, null);
                var animator = actor.GetComponentInChildren<Animator>();
                var body = actor.GetComponentsInChildren<SpriteRenderer>().First(s => s.name == "chushouguai_body");
                for (int reuse = 0; reuse < 3; reuse++)
                {
                    animator.Rebind();
                    animator.Play("Base Layer.chushouguai_dead", 0, 0.9999f);
                    animator.Update(0f);
                    Assert.That(body.color.a, Is.LessThan(0.01f), "Death should still fade out.");
                    pool.Release(actor);
                    actor = pool.Get();
                    actor.ResetForSpawn();
                    foreach (string state in new[] { "idle", "run", "attack_pre", "attack_do" })
                    {
                        animator.Play("Base Layer.chushouguai_" + state, 0, 0f);
                        animator.Update(0.1f);
                        Assert.That(body.color.a, Is.GreaterThan(0.99f), state + " must restore the living body after reuse " + reuse);
                    }
                }
            }
            finally
            {
                pool.Clear();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
