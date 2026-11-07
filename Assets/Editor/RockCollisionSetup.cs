using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShootDemo.EditorTools
{
    public static class RockCollisionSetup
    {
        // Convex silhouettes prevent agents from entering decorative recesses in the rock art.
        [MenuItem("ShootDemo/Scene/Repair Rock Collision Outlines")]
        public static void Repair()
        {
            if (EditorApplication.isPlaying) return;
            int changed = 0;
            foreach (var collider in Object.FindObjectsOfType<PolygonCollider2D>())
            {
                var renderer = collider.GetComponent<SpriteRenderer>();
                if (renderer == null || renderer.sprite == null || renderer.sprite.name != "kuangshan_chang") continue;
                var sprite = renderer.sprite;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
                var pixels = texture.GetPixels32();
                var points = new List<Vector2>();
                var rect = sprite.rect;
                for (int y = (int)rect.yMin; y < rect.yMax; y += 2)
                {
                    int left = -1, right = -1;
                    for (int x = (int)rect.xMin; x < rect.xMax; x++)
                    {
                        if (pixels[y * texture.width + x].a < 240) continue;
                        if (left < 0) left = x;
                        right = x;
                    }
                    if (left < 0) continue;
                    points.Add((new Vector2(left - rect.x, y - rect.y) - sprite.pivot) / sprite.pixelsPerUnit);
                    points.Add((new Vector2(right - rect.x, y - rect.y) - sprite.pivot) / sprite.pixelsPerUnit);
                }
                Object.DestroyImmediate(texture);
                points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
                var hull = new List<Vector2>();
                foreach (var point in points)
                {
                    while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(point);
                }
                int lower = hull.Count;
                for (int i = points.Count - 2; i >= 0; i--)
                {
                    while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], points[i]) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(points[i]);
                }
                if (hull.Count < 4) continue;
                hull.RemoveAt(hull.Count - 1);
                // A small perimeter margin keeps animated limbs out of the rock face.
                for (int i = 0; i < hull.Count; i++)
                {
                    Vector2 point = hull[i];
                    point += point.normalized * 0.2f;
                    if (renderer.flipX) point.x = -point.x;
                    if (renderer.flipY) point.y = -point.y;
                    hull[i] = point;
                }
                Undo.RecordObject(collider, "Repair rock silhouette collider");
                collider.offset = Vector2.zero;
                collider.pathCount = 1;
                collider.SetPath(0, hull.ToArray());
                EditorUtility.SetDirty(collider);
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                changed++;
            }
            Physics2D.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Repaired rock collision silhouettes: " + changed);
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }
}
