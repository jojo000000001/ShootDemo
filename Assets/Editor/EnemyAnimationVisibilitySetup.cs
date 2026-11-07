using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShootDemo.Editor
{
    public static class EnemyAnimationVisibilitySetup
    {
        [MenuItem("Shoot Demo/Repair Enemy Animation Visibility")]
        public static void Build()
        {
            const string folder = "Assets/Animations/Enemies";
            System.IO.Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Arts/guaiwu/chushouguai/anim/chushouguai.controller");
            var clips = source.animationClips.Distinct().ToArray();
            // Explicitly restore channels faded by death. Write Defaults alone does not
            // reliably restore them when a pooled Animator rebinds after its death pose.
            var faded = clips.Where(c => c.name.Contains("_dead"))
                .SelectMany(AnimationUtility.GetCurveBindings)
                .Where(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Color.a")
                .GroupBy(b => b.path).Select(g => g.First()).ToArray();
            string controllerPath = folder + "/ChushouguaiGameplay.overrideController";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(controllerPath);
            if (controller == null)
            {
                controller = new AnimatorOverrideController(source);
                AssetDatabase.CreateAsset(controller, controllerPath);
            }
            var replacements = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            foreach (var original in clips)
            {
                if (original.name.Contains("_dead")) continue;
                string path = folder + "/" + original.name + ".anim";
                var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (copy == null)
                {
                    copy = Object.Instantiate(original);
                    AssetDatabase.CreateAsset(copy, path);
                }
                else EditorUtility.CopySerialized(original, copy);
                var existing = AnimationUtility.GetCurveBindings(original);
                foreach (var binding in faded)
                {
                    // Preserve intentional animated shadows and attack effects.
                    if (existing.Any(b => b.path == binding.path && b.propertyName == binding.propertyName)) continue;
                    AnimationUtility.SetEditorCurve(copy, binding, AnimationCurve.Constant(0f, original.length, 1f));
                }
                EditorUtility.SetDirty(copy);
                replacements.Add(new KeyValuePair<AnimationClip, AnimationClip>(original, copy));
            }
            controller.ApplyOverrides(replacements);
            EditorUtility.SetDirty(controller);
            const string prefabPath = "Assets/Prefabs/Enemies/chushouguai.prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                root.GetComponentInChildren<Animator>(true).runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("Repaired pooled enemy visibility: " + replacements.Count + " living clips.");
        }
    }
}
