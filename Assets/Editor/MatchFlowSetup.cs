using System.Collections.Generic;
using ShootDemo.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ShootDemo.EditorTools
{
    // Saves the authored screens and their nested PlayerHud; does not generate their appearance.
    public static class MatchFlowSetup
    {
        public const string PrefabPath = "Assets/Prefabs/UI/MatchFlow.prefab";

        [MenuItem("ShootDemo/UI/Save Current Screens As Prefab")]
        private static void SaveFromMenu() { Debug.Log(Build()); }

        public static string Build()
        {
            if (EditorApplication.isPlaying) return "Stop Play mode before saving the screens.";
            var flow = Object.FindObjectOfType<MatchFlowScreen>(true);
            if (flow == null) return "No authored MatchFlowScreen found in the scene.";
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

            foreach (var label in flow.GetComponentsInChildren<Text>(true))
            {
                if (label.font != null && EditorUtility.IsPersistent(label.font)) continue;
                var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
                if (font == null) return "Missing project UI font.";
                label.font = font;
                EditorUtility.SetDirty(label);
            }

            var data = new SerializedObject(flow);
            Object state = data.FindProperty("stateMachine").objectReferenceValue;
            data.FindProperty("stateMachine").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            var hud = flow.GetComponentInChildren<PlayerHud>(true);
            var bindings = new Dictionary<string, Object>();
            if (hud != null)
            {
                var hudData = new SerializedObject(hud);
                foreach (string property in new[] { "player", "dash", "session", "state" })
                {
                    bindings[property] = hudData.FindProperty(property).objectReferenceValue;
                    hudData.FindProperty(property).objectReferenceValue = null;
                }
                hudData.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
            }

            bool saved = false;
            try
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(flow.gameObject, PrefabPath, InteractionMode.UserAction, out saved);
            }
            finally
            {
                data = new SerializedObject(flow);
                data.FindProperty("stateMachine").objectReferenceValue = state;
                data.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(flow)) PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
                if (hud != null)
                {
                    var hudData = new SerializedObject(hud);
                    foreach (var binding in bindings) hudData.FindProperty(binding.Key).objectReferenceValue = binding.Value;
                    hudData.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
                }
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
            bool sceneSaved = EditorSceneManager.SaveScene(flow.gameObject.scene);
            return "Screens prefab saved=" + saved + "; scene saved=" + sceneSaved + "; path=" + PrefabPath;
        }
    }
}