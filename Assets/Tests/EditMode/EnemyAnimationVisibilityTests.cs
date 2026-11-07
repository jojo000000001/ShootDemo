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
