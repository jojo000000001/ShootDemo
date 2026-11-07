using System.Reflection;
using NUnit.Framework;
using ShootDemo.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootDemo.Tests.EditMode
{
    public sealed class EnemyNarrowPassageTests
    {
        [Test]
        public void EntranceGuidesAllEnemiesWithoutAnyOccupancyLimit()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("PassageTest");
            SceneManager.MoveGameObjectToScene(root, scene);
            var passage = root.AddComponent<EnemyNarrowPassage>();
            try
            {
                var data = new SerializedObject(passage);
                data.FindProperty("interior").rectValue = new Rect(-1.2f, 0f, 2.4f, 20f);
                data.FindProperty("outside").vector2Value = new Vector2(0f, -1.3f);
                data.FindProperty("entrance").vector2Value = new Vector2(0f, 1f);
                data.ApplyModifiedPropertiesWithoutUndo();
                typeof(EnemyNarrowPassage).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(passage, null);
                for (int i = 0; i < 10; i++)
                {
                    var go = new GameObject("Enemy" + i);
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = new Vector2(0f, -1.3f);
                    var actor = go.AddComponent<EnemyHealth>();
                    Vector2 waypoint;
                    Assert.IsTrue(EnemyNarrowPassage.Guide(actor, Vector2.up * 10f, out waypoint));
                    Assert.That(waypoint, Is.EqualTo(Vector2.up), "Every actor can enter even while others are inside.");
                    go.transform.position = new Vector2(0.08f, -1.2f);
                    Assert.IsTrue(EnemyNarrowPassage.Guide(actor, Vector2.up * 10f, out waypoint));
                    Assert.AreEqual(new Vector2(0f, -1.2f), waypoint,
                        "Align with the entrance before taking a diagonal through the posts.");
                    go.transform.position = new Vector2(0.2f, -0.8f);
                    Assert.IsTrue(EnemyNarrowPassage.Guide(actor, Vector2.up * 10f, out waypoint));
                    Assert.AreEqual(new Vector2(0f, -0.8f), waypoint,
                        "An actor nudged sideways at the mouth must align without retreating into incoming traffic.");
                    foreach (float side in new[] { -3f, 3f })
                    {
                        go.transform.position = new Vector2(side, 8f);
                        Assert.IsTrue(EnemyNarrowPassage.Guide(actor, Vector2.up * 10f, out waypoint));
                        Assert.AreEqual(new Vector2(0f, -1.3f), waypoint,
                            "Actors outside either wall must go around its end before aligning.");
                    }
                    go.transform.position = new Vector2(0.2f, 0.1f);
                    Assert.IsTrue(EnemyNarrowPassage.Guide(actor, Vector2.up * 10f, out waypoint));
                    Assert.AreEqual(Vector2.up, waypoint, "Actors through the mouth must continue inward.");
                    go.transform.position = Vector2.up * 3f;
                    Assert.IsFalse(EnemyNarrowPassage.Guide(actor, Vector2.up * 10f, out waypoint));
                }
                Assert.IsTrue(EnemyNarrowPassage.BlocksSpawn(scene, Vector2.up));
            }
            finally
            {
                typeof(EnemyNarrowPassage).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(passage, null);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
