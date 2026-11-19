using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DungeonProto.EditorTools
{
    [CustomEditor(typeof(DungeonGenerator))]
    public class DungeonGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var gen = (DungeonGenerator)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"생성된 구역: {gen.rooms?.Count ?? 0}", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate")) PrototypeSceneSetup.Regenerate(gen, gen.seed);
                if (GUILayout.Button("New Seed")) PrototypeSceneSetup.Regenerate(gen, Random.Range(1, 999999));
                if (GUILayout.Button("Clear"))
                {
                    gen.Clear();
                    EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
                }
            }
        }
    }

    public static class PrototypeSceneSetup
    {
        public static void Regenerate(DungeonGenerator gen, int seed)
        {
            gen.seed = seed;
            gen.Generate();
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(gen);
            EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
            var player = Object.FindAnyObjectByType<PrototypePlayer>();
            SpawnMarker spawn = null; // 에디트 모드에선 OnEnable 레지스트리가 비어 있으므로 직접 찾는다
            foreach (var m in gen.GetComponentsInChildren<SpawnMarker>())
                if (m.kind == SpawnKind.PlayerSpawn) { spawn = m; break; }
            if (player != null && spawn != null)
                player.transform.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);
        }

        /// <summary>현재 씬에 생성기·플레이어·달빛을 준비하고 맵을 생성한다.</summary>
        [MenuItem("Tools/Dungeon Prototype/Setup Current Scene")]
        public static string SetupCurrentScene()
        {
            var gen = Object.FindAnyObjectByType<DungeonGenerator>();
            if (gen == null) gen = new GameObject("DungeonGenerator").AddComponent<DungeonGenerator>();

            if (GameObject.Find("Moonlight") == null)
            {
                var moon = new GameObject("Moonlight");
                var l = moon.AddComponent<Light>();
                l.type = LightType.Directional;
                l.color = new Color(0.55f, 0.65f, 0.9f);
                l.intensity = 0.35f;
                l.shadows = LightShadows.Soft;
                moon.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            }

            if (Object.FindAnyObjectByType<PrototypePlayer>() == null) CreatePlayer();

            Regenerate(gen, gen.seed);
            return $"rooms={gen.rooms.Count}";
        }

        static void CreatePlayer()
        {
            var p = new GameObject("Player");
            var cc = p.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var camRoot = new GameObject("CameraRoot").transform;
            camRoot.SetParent(p.transform, false);
            camRoot.localPosition = new Vector3(0f, 1.65f, 0f);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(camRoot, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.035f, 0.045f);
            camGo.AddComponent<AudioListener>();

            var flashGo = new GameObject("Flashlight");
            flashGo.transform.SetParent(camGo.transform, false);
            flashGo.transform.localPosition = new Vector3(0.25f, -0.2f, 0f);
            var fl = flashGo.AddComponent<Light>();
            fl.type = LightType.Spot;
            fl.range = 28f;
            fl.spotAngle = 55f;
            fl.innerSpotAngle = 25f;
            fl.intensity = 4f;
            fl.color = new Color(1f, 0.96f, 0.88f);
            fl.shadows = LightShadows.Soft;

            var pp = p.AddComponent<PrototypePlayer>();
            pp.cameraRoot = camRoot;
            pp.flashlight = fl;
        }
    }
}
