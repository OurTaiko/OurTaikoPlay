using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    [CustomEditor(typeof(ResultScene))]
    public sealed class ResultSceneEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var scene = (ResultScene)target;
            EditorGUILayout.LabelField("编辑器界面预览", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || scene.view == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("结算画面")) ProjectBuilder.PreviewResultLayout(scene, 0);
                if (GUILayout.Button("淡入遮罩")) ProjectBuilder.PreviewResultLayout(scene, 1);
                EditorGUILayout.EndHorizontal();
            }
            if (scene.view == null)
                EditorGUILayout.HelpBox("保存场景后，使用 OurTaiko > Apply Result Layout 生成一次可编辑的界面层级。", MessageType.Info);
            else
            {
                EditorGUILayout.HelpBox("预览用示例成绩（おに、过关 45 格）填充已保存界面，不播放动画。魂槽クリア标记按 ResultView.gaugeArt 的难度放置，其他难度在运行时按过关格数平移；云和富士山以保存位置为基准加时间轴位移。进入 Play 后由代码填入实际成绩。", MessageType.Info);
                if (GUILayout.Button("选中界面层级")) Selection.activeGameObject = scene.view.gameObject;
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
