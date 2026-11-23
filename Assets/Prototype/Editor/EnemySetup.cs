using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto.EditorTools
{
    /// <summary>
    /// Tripo 로 만든 Civilian_Male_01 모델을 적으로 쓰기 위한 셋업:
    /// 임포트 설정(루프·Avatar) → Animator Controller → 적 프리팹 → 씬의 EnemyDirector 연결.
    /// </summary>
    public static class EnemySetup
    {
        public const string FbxPath = "Assets/Prototype/Models/Civilian_Male_01/tripo_convert_194dae62-3531-4610-a063-eb18784e6b06.fbx";
        const string ControllerPath = "Assets/Prototype/Animation/Enemy_Civilian.controller";
        const string PrefabPath = "Assets/Prototype/Prefabs/Enemy_Civilian.prefab";

        const float TargetHeight = 1.78f;   // 원본은 약 1.1m → 성인 키로 확대
        const float PatrolSpeed = 1.6f;
        const float ChaseSpeed = 4.2f;
        // 원본(1.1m) 클립의 제자리 보폭 속도 측정값: 4걸음 × 발 이동폭 / 길이
        const float WalkNaturalSpeed = 4f * 0.37f / 2.33f;
        const float RunNaturalSpeed = 4f * 0.44f / 1.25f;

        [MenuItem("Tools/Dungeon Prototype/Build Civilian Enemy")]
        public static string BuildAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine(FixImport());
            var ctrl = BuildController(out float scale, out string ctrlInfo);
            sb.AppendLine(ctrlInfo);
            sb.AppendLine(BuildPrefab(ctrl, scale));
            // 컨트롤러를 지우고 다시 만들면 같은 세션에서 프리팹 참조가 null 로 남는 경우가 있어 강제 재임포트
            AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            var check = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponentInChildren<Animator>();
            sb.AppendLine($"verify: prefab controller={(check.runtimeAnimatorController != null ? check.runtimeAnimatorController.name : "NULL")}");
            sb.AppendLine(HookIntoScene());
            return sb.ToString();
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }

        static string Pretty(string take)
        {
            var n = take.Split('.')[0];
            return char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        public static string FixImport()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            imp.animationType = ModelImporterAnimationType.Generic;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = Pretty(c.takeName);
                c.loopTime = c.name == "Idle" || c.name == "Walk" || c.name == "Run";
                c.loopPose = false;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            return "import: Generic + Avatar 생성, 클립 " + string.Join(", ", clips.Select(c => $"{c.name}(loop={c.loopTime})"));
        }

        static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().First(c => c.name == name);

        static AnimatorController BuildController(out float scale, out string info)
        {
            EnsureFolder("Assets", "Prototype");
            EnsureFolder("Assets/Prototype", "Animation");
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);

            MeasureModel(out float height, out float minY);
            scale = TargetHeight / height;
            float walkScaled = WalkNaturalSpeed * scale, runScaled = RunNaturalSpeed * scale;

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Catch", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;

            var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Idle"), 0f);
            tree.AddChild(Clip("Walk"), PatrolSpeed);
            tree.AddChild(Clip("Run"), ChaseSpeed);
            var ch = tree.children;
            ch[1].timeScale = PatrolSpeed / walkScaled;  // 발 미끄러짐 보정
            ch[2].timeScale = ChaseSpeed / runScaled;
            tree.children = ch;
            sm.defaultState = loco;

            var catchState = sm.AddState("Catch");
            catchState.motion = Clip("Jump");
            catchState.speed = 1.6f;
            var toCatch = sm.AddAnyStateTransition(catchState);
            toCatch.AddCondition(AnimatorConditionMode.If, 0f, "Catch");
            toCatch.duration = 0.1f;
            toCatch.canTransitionToSelf = false;
            var back = catchState.AddTransition(loco);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.duration = 0.25f;

            AssetDatabase.SaveAssets();
            info = $"controller: height={height:F2}m minY={minY:F3} → scale={scale:F2}, walk timeScale={ch[1].timeScale:F2}, run timeScale={ch[2].timeScale:F2}";
            return ctrl;
        }

        /// <summary>Idle 첫 프레임 메시를 구워 실제 키와 발바닥 높이를 잰다.</summary>
        static void MeasureModel(out float height, out float minY)
        {
            var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath));
            Clip("Idle").SampleAnimation(go, 0f);
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            var m = smr.transform.localToWorldMatrix;
            minY = float.MaxValue; float maxY = float.MinValue;
            foreach (var v in mesh.vertices)
            {
                float y = m.MultiplyPoint3x4(v).y;
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            height = maxY - minY;
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(go);
        }

        static string BuildPrefab(AnimatorController ctrl, float scale)
        {
            EnsureFolder("Assets/Prototype", "Prefabs");
            MeasureModel(out _, out float minY);

            var root = new GameObject("Enemy_Civilian");
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.speed = PatrolSpeed;
            agent.angularSpeed = 420f;
            agent.acceleration = 14f;
            agent.stoppingDistance = 0.3f;

            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f);
            col.height = 1.8f;
            col.radius = 0.3f;

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = new Vector3(0f, -minY * scale, 0f); // 발바닥을 바닥에 맞춤

            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Avatar>().FirstOrDefault();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var ai = root.AddComponent<EnemyAI>();
            ai.animator = anim; // 속도는 PrototypeSettings 의 "적"에서 정한다

            string avatarInfo = anim.avatar != null ? anim.avatar.name + " valid=" + anim.avatar.isValid : "none";
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return $"prefab: {PrefabPath} (avatar={avatarInfo}, footOffset={-minY * scale:F3})";
        }

        static string HookIntoScene()
        {
            var gen = Object.FindAnyObjectByType<DungeonGenerator>();
            if (gen == null) return "scene: DungeonGenerator 없음 — 씬 연결 건너뜀";
            var dir = gen.GetComponent<EnemyDirector>();
            if (dir == null) dir = gen.gameObject.AddComponent<EnemyDirector>();
            dir.enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            EditorUtility.SetDirty(dir);
            EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
            return $"scene: EnemyDirector 연결 (prefab={dir.enemyPrefab != null})";
        }
    }
}
