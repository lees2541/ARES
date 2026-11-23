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
            var gen = (DungeonGenerator)target;
            var owner = gen.GetComponent<PrototypeGame>();
            if (owner != null && owner.settings != null)
            {
                EditorGUILayout.HelpBox(
                    "맵 크기(구역 수, 가로·세로, 순환 통로, 시드)와 적 수는 게임 설정 에셋의 \"맵\", \"적\"에서 정한다.\n" +
                    (owner.settings.map.generateOnPlay
                        ? "Play 를 누르면 그 값으로 던전을 자동으로 만든다. 아래 Layout 값은 마지막으로 만든 맵의 값이다.\n미리 보려면 Generate 를 누른다 (씬을 저장하면 맵도 함께 저장돼 씬 파일이 커진다)."
                        : "자동 생성이 꺼져 있어 씬에 만들어 둔 맵을 그대로 쓴다. Generate 를 누르면 설정 값으로 다시 만든다."),
                    MessageType.Info);
                if (GUILayout.Button("게임 설정 열기")) PrototypeSettingsEditor.OpenSettings();
                EditorGUILayout.Space();
            }
            DrawDefaultInspector();
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
            // 맵 크기의 원본은 게임 설정 에셋이다
            var game = gen.GetComponent<PrototypeGame>();
            if (game != null && game.settings != null) gen.ApplySettings(game.settings, seed);
            else gen.seed = seed;
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

            // 구조 루프: 게임 상태 관리자, 민간인 수 관리자, 화면 표시를 생성기와 같은 오브젝트에 붙이고 설정 에셋을 연결한다
            var game = gen.GetComponent<PrototypeGame>();
            if (game == null) game = gen.gameObject.AddComponent<PrototypeGame>();
            if (game.settings == null) game.settings = PrototypeSettingsEditor.LoadOrCreate();
            if (gen.GetComponent<CivilianDirector>() == null) gen.gameObject.AddComponent<CivilianDirector>();
            if (gen.GetComponent<PrototypeHud>() == null) gen.gameObject.AddComponent<PrototypeHud>();
            EditorUtility.SetDirty(game);

            if (game.settings.map.generateOnPlay)
            {
                // Play 때 자동으로 만들므로 씬에는 맵을 담아 두지 않는다 (씬 파일이 작게 유지된다)
                gen.Clear();
                EditorUtility.SetDirty(gen);
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
                return $"auto-generate on play, settings={AssetDatabase.GetAssetPath(game.settings)}";
            }
            Regenerate(gen, gen.seed);
            return $"rooms={gen.rooms.Count}, settings={AssetDatabase.GetAssetPath(game.settings)}";
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
