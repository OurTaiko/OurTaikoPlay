using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    [CustomEditor(typeof(SongSelectScene))]
    public sealed class SongSelectSceneEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var scene = (SongSelectScene)target;
            EditorGUILayout.LabelField("编辑器界面预览", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || scene.view == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("选曲列表")) ProjectBuilder.PreviewSongSelectLayout(scene, 0);
                if (GUILayout.Button("难度选择")) ProjectBuilder.PreviewSongSelectLayout(scene, 1);
                if (GUILayout.Button("演奏选项")) ProjectBuilder.PreviewSongSelectLayout(scene, 2);
                EditorGUILayout.EndHorizontal();
            }
            if (scene.view == null)
                EditorGUILayout.HelpBox("保存场景后，使用 OurTaiko > Apply Song Select Layout 生成一次可编辑的界面层级。", MessageType.Info);
            else
            {
                EditorGUILayout.HelpBox("预览切换已保存界面的显示状态，BestScore 使用示例成绩。列表以收起布局显示，不播放动画；进入 Play 后由代码更新歌曲、输入和动画。", MessageType.Info);
                if (GUILayout.Button("打开演奏选项 Prefab"))
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(ProjectBuilder.PlayOptionsPrefabPath));
                if (GUILayout.Button("选中界面层级")) Selection.activeGameObject = scene.view.gameObject;
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
