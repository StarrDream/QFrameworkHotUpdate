using UnityEngine;
using UnityEditor;

namespace QHotUpdateSystem.Editor.Windows.Styles
{
    public static class HotUpdateEditorStyles
    {
        static GUIStyle _header;
        public static GUIStyle Header
        {
            get
            {
                if (_header == null)
                {
                    _header = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 13
                    };
                }
                return _header;
            }
        }

        static GUIStyle _box;
        public static GUIStyle Box => _box ?? (_box = new GUIStyle("HelpBox"));

        static GUIStyle _toolbarButton;
        public static GUIStyle ToolbarButton => _toolbarButton ?? (_toolbarButton = new GUIStyle(EditorStyles.miniButton));

        static GUIStyle _foldout;
        public static GUIStyle Foldout => _foldout ?? (_foldout = new GUIStyle(EditorStyles.foldout));
    }
}