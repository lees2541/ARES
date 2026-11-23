using UnityEditor;
using UnityEngine;

namespace DungeonProto.EditorTools
{
    /// <summary>[Setting] 이 붙은 값을 한글 이름·설명과 함께 그린다. 범위가 있으면 슬라이더로 보여 준다.</summary>
    [CustomPropertyDrawer(typeof(SettingAttribute))]
    public class SettingDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var a = (SettingAttribute)attribute;
            var content = new GUIContent(a.label, a.tip ?? string.Empty);

            if (a.hasRange && property.propertyType == SerializedPropertyType.Float)
                EditorGUI.Slider(position, property, a.min, a.max, content);
            else if (a.hasRange && property.propertyType == SerializedPropertyType.Integer)
                EditorGUI.IntSlider(position, property, Mathf.RoundToInt(a.min), Mathf.RoundToInt(a.max), content);
            else
                EditorGUI.PropertyField(position, property, content, true);
        }
    }

    /// <summary>게임 설정 에셋의 인스펙터. 위에 안내를 붙이고 묶음을 한 번에 펴고 접는 버튼을 둔다.</summary>
    [CustomEditor(typeof(PrototypeSettings))]
    public class PrototypeSettingsEditor : Editor
    {
        public const string AssetPath = "Assets/Prototype/Settings/PrototypeSettings.asset";

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "프로토타입 게임 설정. Play 를 누르기 전에 여기서 값을 바꾼다.\n" +
                "이름 위에 마우스를 올리면 설명이 나온다. 기획에서 정하지 않은 수치는 임시 기본값이다.\n" +
                "Play 중에 바꾼 값도 바로 적용되고, 에셋이므로 Play 를 끝내도 남는다.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("모두 펴기")) SetExpanded(true);
                if (GUILayout.Button("모두 접기")) SetExpanded(false);
            }
            EditorGUILayout.Space();

            serializedObject.Update();
            var p = serializedObject.GetIterator();
            bool enterChildren = true;
            while (p.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (p.propertyPath == "m_Script") continue;
                EditorGUILayout.PropertyField(p, true);
            }
            serializedObject.ApplyModifiedProperties();
        }

        void SetExpanded(bool expanded)
        {
            var p = serializedObject.GetIterator();
            bool enterChildren = true;
            while (p.NextVisible(enterChildren))
            {
                enterChildren = false;
                p.isExpanded = expanded;
            }
        }

        /// <summary>게임 설정 에셋을 인스펙터에 연다. 없으면 만든다.</summary>
        [MenuItem("Tools/Dungeon Prototype/Open Game Settings")]
        public static void OpenSettings()
        {
            var asset = LoadOrCreate();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        public static PrototypeSettings LoadOrCreate()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PrototypeSettings>(AssetPath);
            if (asset != null) return asset;
            if (!AssetDatabase.IsValidFolder("Assets/Prototype/Settings")) AssetDatabase.CreateFolder("Assets/Prototype", "Settings");
            asset = ScriptableObject.CreateInstance<PrototypeSettings>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
