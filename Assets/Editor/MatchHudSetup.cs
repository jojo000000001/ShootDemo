using System.Collections.Generic;
using ShootDemo.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ShootDemo.EditorTools
{
    // Exports the authored HUD. Layout and styling live in the prefab.
    public static class MatchHudSetup
    {
        public const string PrefabPath = "Assets/Prefabs/UI/PlayerHud.prefab";

        [MenuItem("ShootDemo/UI/Save Current HUD As Prefab")]
        private static void SaveFromMenu() { Debug.Log(Build()); }

        public static string Build()
        {
            if (EditorApplication.isPlaying) return "Stop Play mode before saving the HUD prefab.";
            var hud = Object.FindObjectOfType<PlayerHud>(true);
            if (hud == null) return "No authored PlayerHud found in the scene.";
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

            // Persist existing fonts once instead of replacing them at runtime.
            foreach (var label in hud.GetComponentsInChildren<Text>(true))
            {
                if (label.font != null && EditorUtility.IsPersistent(label.font)) continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
                if (font == null) return "Missing project UI font.";
                label.font = font;
                EditorUtility.SetDirty(label);
            }

            var data = new SerializedObject(hud);
            foreach (string property in new[] { "healthFill", "dashFill" })
            {
                var fill = data.FindProperty(property).objectReferenceValue as Image;
                if (fill == null) continue;
                fill.type = Image.Type.Sliced;
                RectTransform rect = fill.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            // Scene data remains on the instance; prefab contains only its own widget references.
            var bindings = new Dictionary<string, Object>();
            foreach (string property in new[] { "player", "dash", "session", "state", "resultTitle" })
            {
                bindings[property] = data.FindProperty(property).objectReferenceValue;
                data.FindProperty(property).objectReferenceValue = null;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            bool saved = false;
            try
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(hud.gameObject, PrefabPath, InteractionMode.UserAction, out saved);
            }
            finally
            {
                data = new SerializedObject(hud);
                foreach (var binding in bindings) data.FindProperty(binding.Key).objectReferenceValue = binding.Value;
                data.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(hud)) PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            bool sceneSaved = EditorSceneManager.SaveScene(hud.gameObject.scene);
            return "HUD prefab saved=" + saved + "; scene saved=" + sceneSaved + "; path=" + PrefabPath;
        }
    }
}
