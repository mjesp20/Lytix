using UnityEditor;
using UnityEngine;

namespace LytixInternal
{
    public class LytixFeedbackInspectorWindow : EditorWindow
    {
        private LytixEntry.Entry _entry;
        private string                     _fullNote;
        private Vector2                    _scroll;
    
        public static void Show(LytixEntry.Entry entry, string fullNote)
        {
            var win = GetWindow<LytixFeedbackInspectorWindow>(true, "Feedback Note", true);
            win._entry    = entry;
            win._fullNote = fullNote;
            win._scroll   = Vector2.zero;
            win.minSize   = new Vector2(320, 200);
            win.Show();
        }

        void OnGUI()
        {
            if (_entry == null) return;

            GUILayout.Space(8);
            GUILayout.Label("Feedback Note", EditorStyles.boldLabel);
            DrawLine();

            // Position
            GUILayout.Space(4);
            GUILayout.Label("Position", EditorStyles.miniBoldLabel);
            Vector3 pos = _entry.position.ToVector3();
            EditorGUILayout.LabelField("X", pos.x.ToString("F2"));
            EditorGUILayout.LabelField("Y", pos.y.ToString("F2"));
            EditorGUILayout.LabelField("Z", pos.z.ToString("F2"));

            DrawLine();

            // Full note text
            GUILayout.Space(4);
            GUILayout.Label("Note", EditorStyles.miniBoldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, EditorStyles.helpBox, GUILayout.MinHeight(80));
            GUILayout.Label(_fullNote, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();

            // Any other args
            if (_entry.args != null && _entry.args.Count > 1)
            {
                DrawLine();
                GUILayout.Space(4);
                GUILayout.Label("Other Data", EditorStyles.miniBoldLabel);
                foreach (var kvp in _entry.args)
                {
                    if (kvp.Key == "note") continue;
                    EditorGUILayout.LabelField(kvp.Key, kvp.Value?.ToString() ?? "null");
                }
            }

            GUILayout.Space(8);
        }

        private void DrawLine()
        {
            GUILayout.Space(4);
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.35f, 0.35f, 0.35f, 1f));
            GUILayout.Space(4);
        }
    }
}