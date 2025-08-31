using UnityEngine;
using UnityEditor;

namespace QHotUpdateSystem.Editor.Windows.Components
{
    /// <summary>
    /// 简易可拖拽分割条（水平：左右面板）
    /// </summary>
    public static class SplitterGUILayout
    {
        private static bool _dragging;
        private static float _dragStart;
        private static float _orig;
        private static readonly Color SplitLineColor = new Color(0,0,0,0.25f);

        public static float HorizontalSplitter(float current, float min, float max, string prefsKey = null)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.ExpandWidth(true));
            // 实际不在这里画，使用 Layout 拿到总区域后再绘制
            float splitterX = current;
            var ev = Event.current;
            var full = r;
            full.height = EditorGUIUtility.singleLineHeight;
            // 仅用高度 4 的区域便于拖拽（不可见）
            Rect dragRect = new Rect(splitterX - 2, full.y, 4, Screen.height);

            EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.ResizeHorizontal);
            if (ev.type == EventType.MouseDown && dragRect.Contains(ev.mousePosition))
            {
                _dragging = true;
                _dragStart = ev.mousePosition.x;
                _orig = current;
                ev.Use();
            }
            if (_dragging && ev.type == EventType.MouseDrag)
            {
                current = Mathf.Clamp(_orig + (ev.mousePosition.x - _dragStart), min, max);
                ev.Use();
                if (!string.IsNullOrEmpty(prefsKey))
                    EditorPrefs.SetFloat(prefsKey, current);
            }
            if (_dragging && (ev.type == EventType.MouseUp || ev.rawType == EventType.MouseUp))
            {
                _dragging = false;
                ev.Use();
            }

            // 绘制分割线
            Handles.color = SplitLineColor;
            Handles.DrawLine(new Vector2(splitterX, 0), new Vector2(splitterX, Screen.height));
            Handles.color = Color.white;

            return current;
        }
    }
}
