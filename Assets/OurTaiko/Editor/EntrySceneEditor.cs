using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    [CustomEditor(typeof(EntryScene))]
    public sealed class EntrySceneEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var scene = (EntryScene)target;
            EditorGUILayout.LabelField("编辑器界面预览", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || scene.view == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("投币画面")) ProjectBuilder.PreviewEntryLayout(scene, 0);
                if (GUILayout.Button("模式选择")) ProjectBuilder.PreviewEntryLayout(scene, 1);
                EditorGUILayout.EndHorizontal();
            }
            if (scene.view == null)
                EditorGUILayout.HelpBox("保存场景后，使用 OurTaiko > Create Entry Scene 生成一次可编辑的界面层级。", MessageType.Info);
            else
            {
                EditorGUILayout.HelpBox("预览只切换已保存界面的显示与透明度，不播放动画。模式板保存在初始布局（第一块打开居中，其余关闭在下方槽位），滑动时以保存位置为基准偏移；标题在 TitleClosed 与 TitleOpen 标记之间移动。进入 Play 后由代码驱动动画与输入。", MessageType.Info);
                if (GUILayout.Button("选中界面层级")) Selection.activeGameObject = scene.view.gameObject;
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
