using ShootDemo.Core;
using ShootDemo.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ShootDemo.EditorTools
{
    public static class StressTestSetup
    {
        [MenuItem("ShootDemo/UI/Setup D4 Stress Controls")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) return;
            var state = Object.FindObjectOfType<GameStateMachine>();
            if (state.GetComponent<StressTestController>() == null) state.gameObject.AddComponent<StressTestController>();
            const string path = "Assets/Prefabs/UI/MatchFlow.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var flow = root.GetComponent<MatchFlowScreen>();
                var data = new SerializedObject(flow);
                var menu = (GameObject)data.FindProperty("menuPanel").objectReferenceValue;
                var start = (Button)data.FindProperty("startButton").objectReferenceValue;
                var quit = (Button)data.FindProperty("quitButton").objectReferenceValue;
                var old = menu.transform.Find("StressEntry");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var oldControls = root.transform.Find("StressControls");
                if (oldControls != null) Object.DestroyImmediate(oldControls.gameObject);
                var binding = root.GetComponent<StressTestScreen>();
                if (binding == null) binding = root.AddComponent<StressTestScreen>();

                start.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 112);
                quit.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -112);
                Button entry = CloneButton(start, menu.transform, "StressEntry", "压力测试", Vector2.zero, new Vector2(460, 96));

                var controls = new GameObject("StressControls", typeof(RectTransform));
                controls.layer = 5;
                controls.transform.SetParent(root.transform, false);
                var rect = controls.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
                rect.anchoredPosition = new Vector2(-28, 28);
                rect.sizeDelta = new Vector2(670, 125);
                Button b100 = CloneButton(start, controls.transform, "Tier100", "100", new Vector2(75, -85), new Vector2(130, 58));
                Button b500 = CloneButton(start, controls.transform, "Tier500", "500", new Vector2(215, -85), new Vector2(130, 58));
                Button b1000 = CloneButton(start, controls.transform, "Tier1000", "1000", new Vector2(355, -85), new Vector2(130, 58));
                Button exit = CloneButton(start, controls.transform, "ExitStress", "退出压测", new Vector2(550, -85), new Vector2(230, 58));
                var labelObject = new GameObject("Population", typeof(RectTransform), typeof(Text));
                labelObject.layer = 5;
                labelObject.transform.SetParent(controls.transform, false);
                var lr = labelObject.GetComponent<RectTransform>();
                lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0, 1);
                lr.anchoredPosition = new Vector2(0, 0);
                lr.sizeDelta = new Vector2(670, 48);
                var label = labelObject.GetComponent<Text>();
                label.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
                label.fontSize = 30;
                label.alignment = TextAnchor.MiddleRight;
                label.color = new Color(0.08f, 0.1f, 0.23f);
                label.text = "压测：0 / 100";
                label.raycastTarget = false;
                var bindings = new SerializedObject(binding);
                Set(bindings, "entry", entry);
                Set(bindings, "tier100", b100);
                Set(bindings, "tier500", b500);
                Set(bindings, "tier1000", b1000);
                Set(bindings, "exit", exit);
                Set(bindings, "controls", controls);
                Set(bindings, "status", label);
                bindings.ApplyModifiedPropertiesWithoutUndo();
                controls.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            EditorSceneManager.MarkSceneDirty(state.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
        }

        private static void Set(SerializedObject target, string name, Object value) => target.FindProperty(name).objectReferenceValue = value;

        private static Button CloneButton(Button template, Transform parent, string name, string caption, Vector2 position, Vector2 size)
        {
            var button = Object.Instantiate(template, parent);
            button.name = name;
            button.onClick = new Button.ButtonClickedEvent();
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            if (parent.name == "StressControls") rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var label = button.GetComponentInChildren<Text>(true);
            label.text = caption;
            label.fontSize = parent.name == "StressControls" ? 28 : 40;
            return button;
        }
    }
}
