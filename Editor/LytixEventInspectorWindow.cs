using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LytixInternal
{
    public class LytixEventInspectorWindow : EditorWindow
    {
        private LytixEntry.Entry _entry;
        private int _playerIndex;
        private Color _playerColor;
        private Vector2 _scroll;

        public static void Show(LytixEntry.Entry entry, int playerIndex, Color playerColor)
        {
            LytixEventInspectorWindow window = GetWindow<LytixEventInspectorWindow>(true, "Event Inspector", true);
            window._entry = entry;
            window._playerIndex = playerIndex;
            window._playerColor = playerColor;
            window._scroll = Vector2.zero;
            window.minSize = new Vector2(280, 200);
            window.Show();
        }

        void OnGUI()
        {
            if (_entry == null)
            {
                GUILayout.Label("No entry selected.", EditorStyles.centeredGreyMiniLabel);
                return;
            }


            GUILayout.Space(8);

            Rect colorRect = EditorGUILayout.GetControlRect(false, 6f);
            EditorGUI.DrawRect(colorRect, _playerColor);

            GUILayout.Space(4);
            GUILayout.Label($"Player {_playerIndex + 1}", EditorStyles.boldLabel);
            DrawHorizontalLine();

            GUILayout.Space(4);
            GUILayout.Label("Position", EditorStyles.miniBoldLabel);
            Vector3 pos = _entry.position.ToVector3();
            EditorGUILayout.LabelField("X", pos.x.ToString("F3"));
            EditorGUILayout.LabelField("Y", pos.y.ToString("F3"));
            EditorGUILayout.LabelField("Z", pos.z.ToString("F3"));

            DrawHorizontalLine();

            GUILayout.Space(4);
            GUILayout.Label("Event Data", EditorStyles.miniBoldLabel);

            if (_entry.args == null || _entry.args.Count == 0)
            {
                GUILayout.Label("No args.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (KeyValuePair<string, object> kvp in _entry.args)
            {
                EditorGUILayout.LabelField(kvp.Key, kvp.Value?.ToString() ?? "(null)");
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(8);
        }

        private void DrawHorizontalLine(float topSpacing = 4f, float bottomSpacing = 4f)
        {
            GUILayout.Space(topSpacing);
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.35f, 0.35f, 0.35f, 1f));
            GUILayout.Space(bottomSpacing);
        }
    }
}