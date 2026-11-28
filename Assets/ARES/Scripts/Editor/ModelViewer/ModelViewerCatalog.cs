using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARES.EditorTools
{
    /// <summary>애니메이션 클립의 종류. 어떤 모델에서 재생되는지가 종류마다 다르다.</summary>
    internal enum ClipKind
    {
        /// <summary>Humanoid 아바타가 있는 모델이면 뼈 이름이 달라도 재생된다.</summary>
        Humanoid,
        /// <summary>뼈 경로(이름)가 같은 모델에서만 재생된다.</summary>
        Generic,
        /// <summary>옛 Animation 컴포넌트용. 뼈 경로가 같아야 한다.</summary>
        Legacy,
    }

    /// <summary>모델 목록의 한 줄.</summary>
    internal sealed class ModelEntry
    {
        public string path;
        public string name;
        public bool isPrefab;
    }

    /// <summary>클립 목록의 한 줄.</summary>
    internal sealed class ClipEntry
    {
        public AnimationClip clip;
        public string path;
        public string name;
        public ClipKind kind;
        /// <summary>FBX 같은 모델 파일 안에 든 클립이면 true. 이런 클립은 임포트 설정으로 종류를 바꿀 수 있다.</summary>
        public bool fromModelFile;
    }

    /// <summary>모델 하나를 살펴본 결과. 모델 뷰어의 정보 칸에 쓴다.</summary>
    internal sealed class ModelInfo
    {
        public Vector3 size;
        public float minY;
        public int triangles;
        public int vertices;
        public int materials;
        public int renderers;
        public int skinnedRenderers;
        public int bones;
        public bool hasAnimator;
        public bool isHumanoid;
        public bool avatarValid;
        public string rigLabel;
        public Vector3 rootScale;
    }

    /// <summary>
    /// 모델 뷰어가 쓰는 조회 기능: 프로젝트에서 모델과 클립을 찾고, 모델을 재고, 클립이 모델에 맞는지 본다.
    /// 화면을 그리지 않는 순수한 조회만 둔다.
    /// </summary>
    internal static class ModelViewerCatalog
    {
        const string PreviewClipPrefix = "__preview__";

        /// <summary>폴더 경로가 프로젝트에 없으면 Assets 로 바꿔 돌려준다.</summary>
        public static string ValidFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "Assets";
            folder = folder.Replace('\\', '/').TrimEnd('/');
            return AssetDatabase.IsValidFolder(folder) ? folder : "Assets";
        }

        /// <summary>폴더 아래의 모델 파일(FBX 등)을 찾는다. includePrefabs 면 프리팹도 넣는다.</summary>
        public static List<ModelEntry> FindModels(string folder, bool includePrefabs)
        {
            var result = new List<ModelEntry>();
            var seen = new HashSet<string>();
            var folders = new[] { ValidFolder(folder) };
            AddModels(result, seen, AssetDatabase.FindAssets("t:Model", folders), false);
            if (includePrefabs) AddModels(result, seen, AssetDatabase.FindAssets("t:Prefab", folders), true);
            result.Sort((a, b) => string.CompareOrdinal(a.name.ToLowerInvariant(), b.name.ToLowerInvariant()));
            return result;
        }

        static void AddModels(List<ModelEntry> result, HashSet<string> seen, string[] guids, bool isPrefab)
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                result.Add(new ModelEntry { path = path, name = Path.GetFileNameWithoutExtension(path), isPrefab = isPrefab });
            }
        }

        /// <summary>폴더 아래의 애니메이션 클립을 찾는다. 모델 파일 안에 든 클립도 포함한다.</summary>
        public static List<ClipEntry> FindClips(string folder)
        {
            var result = new List<ClipEntry>();
            var seen = new HashSet<string>();
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { ValidFolder(folder) });
            bool showProgress = guids.Length > 300;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                    if (showProgress && (i & 31) == 0)
                        EditorUtility.DisplayProgressBar("Model Viewer", "애니메이션 클립을 찾는 중", (float)i / guids.Length);
                    AddClipsAtPath(result, path);
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
            result.Sort((a, b) => string.CompareOrdinal(a.name.ToLowerInvariant(), b.name.ToLowerInvariant()));
            return result;
        }

        /// <summary>파일 하나에 든 클립을 목록에 더한다. 임포터가 만드는 미리보기용 클립은 뺀다.</summary>
        public static void AddClipsAtPath(List<ClipEntry> result, string path)
        {
            if (path.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip != null) result.Add(MakeEntry(clip, path, false));
                return;
            }
            foreach (Object obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                if (obj is AnimationClip clip && !clip.name.StartsWith(PreviewClipPrefix))
                    result.Add(MakeEntry(clip, path, true));
            }
        }

        static ClipEntry MakeEntry(AnimationClip clip, string path, bool fromModelFile)
        {
            ClipKind kind = clip.legacy ? ClipKind.Legacy : (clip.isHumanMotion ? ClipKind.Humanoid : ClipKind.Generic);
            return new ClipEntry { clip = clip, path = path, name = clip.name, kind = kind, fromModelFile = fromModelFile };
        }

        /// <summary>
        /// 클립이 움직이는 뼈 경로 가운데 이 모델에 실제로 있는 것의 비율(0~1).
        /// Generic·Legacy 클립이 이 모델에서 재생될지 가늠하는 데 쓴다.
        /// </summary>
        public static float PathMatchRatio(AnimationClip clip, Transform root)
        {
            if (clip == null || root == null) return 0f;
            var paths = new HashSet<string>();
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type == typeof(Transform)) paths.Add(binding.path);
            }
            if (paths.Count == 0) return 0f;
            int matched = 0;
            foreach (string path in paths)
            {
                if (path.Length == 0 || root.Find(path) != null) matched++;
            }
            return (float)matched / paths.Count;
        }

        /// <summary>미리보기로 세운 모델을 재서 정보로 만든다.</summary>
        public static ModelInfo Analyze(GameObject instance, string assetPath)
        {
            var info = new ModelInfo { rootScale = instance.transform.localScale };
            var materials = new HashSet<Material>();
            bool hasBounds = false;
            var bounds = new Bounds(instance.transform.position, Vector3.zero);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = skinned.sharedMesh;
                    info.skinnedRenderers++;
                    info.bones = Mathf.Max(info.bones, skinned.bones.Length);
                }
                else if (renderer is MeshRenderer)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) mesh = filter.sharedMesh;
                }
                else
                {
                    continue;
                }
                info.renderers++;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null) materials.Add(material);
                }
                if (mesh != null)
                {
                    info.vertices += mesh.vertexCount;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        if (mesh.GetTopology(sub) == MeshTopology.Triangles) info.triangles += (int)(mesh.GetIndexCount(sub) / 3);
                    }
                }
                if (hasBounds) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; hasBounds = true; }
            }
            info.materials = materials.Count;
            info.size = hasBounds ? bounds.size : Vector3.zero;
            info.minY = hasBounds ? bounds.min.y : 0f;

            var animator = instance.GetComponentInChildren<Animator>(true);
            info.hasAnimator = animator != null;
            if (animator != null && animator.avatar != null)
            {
                info.avatarValid = animator.avatar.isValid;
                info.isHumanoid = animator.avatar.isHuman && animator.avatar.isValid;
            }
            info.rigLabel = RigLabel(info, assetPath, instance);
            return info;
        }

        static string RigLabel(ModelInfo info, string assetPath, GameObject instance)
        {
            if (info.isHumanoid) return "Humanoid (아바타 유효)";
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer != null)
            {
                switch (importer.animationType)
                {
                    case ModelImporterAnimationType.Human: return "Humanoid (아바타가 유효하지 않음)";
                    case ModelImporterAnimationType.Generic: return "Generic";
                    case ModelImporterAnimationType.Legacy: return "Legacy";
                    default: return info.skinnedRenderers > 0 ? "리그 임포트 안 함 (None)" : "리그 없음";
                }
            }
            if (instance.GetComponentInChildren<Animation>(true) != null) return "Legacy";
            if (info.hasAnimator) return "Generic";
            return info.skinnedRenderers > 0 ? "뼈는 있고 Animator 는 없음" : "리그 없음";
        }

        /// <summary>정보 칸에 보여 줄 글.</summary>
        public static string Describe(ModelInfo info)
        {
            var sb = new StringBuilder();
            sb.Append("크기  가로 ").Append(info.size.x.ToString("0.00"))
              .Append(" · 높이 ").Append(info.size.y.ToString("0.00"))
              .Append(" · 깊이 ").Append(info.size.z.ToString("0.00")).Append(" m\n");
            sb.Append("삼각형 ").Append(info.triangles.ToString("N0"))
              .Append(" · 정점 ").Append(info.vertices.ToString("N0"))
              .Append(" · 머티리얼 ").Append(info.materials)
              .Append(" · 렌더러 ").Append(info.renderers).Append('\n');
            sb.Append("리그  ").Append(info.rigLabel);
            if (info.bones > 0) sb.Append(" · 뼈 ").Append(info.bones);
            return sb.ToString();
        }
    }
}
