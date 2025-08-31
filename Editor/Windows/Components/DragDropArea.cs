using UnityEngine;
using UnityEditor;

namespace QHotUpdateSystem.Editor.Windows.Components
{
    /// <summary>
    /// 文件/目录拖拽区域
    /// </summary>
    public static class DragDropArea
    {
        public static string[] Draw(Rect rect, string label)
        {
            GUI.Box(rect, label, EditorStyles.helpBox);
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return null;

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    return DragAndDrop.paths;
                }
                evt.Use();
            }
            return null;
        }
    }
}