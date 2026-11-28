using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ARES.EditorTools
{
    /// <summary>
    /// 모델과 애니메이션을 골라서 바로 보는 창. 메뉴: Tools > ARES > Model Viewer
    /// 왼쪽 목록에서 모델과 클립을 누르면 오른쪽 미리보기에 재생된다. 열린 씬은 건드리지 않는다.
    /// 새로 만든 모델의 크기·리그와, 받아 온 애니메이션이 그 모델에 맞는지 확인하는 데 쓴다.
    /// </summary>
    public sealed class ModelViewerWindow : EditorWindow
    {
        const string MenuPath = "Tools/ARES/Model Viewer";
        const string AssetMenuPath = "Assets/ARES/Open in Model Viewer";
        const float RowHeight = 18f;
        const float LeftWidth = 300f;
        const float CompatibleRatio = 0.5f;

        [SerializeField] private string searchFolder = "Assets";
        [SerializeField] private bool includePrefabs;
        [SerializeField] private bool followSelection = true;
        [SerializeField] private string modelPath = "";
        [SerializeField] private string clipPath = "";
        [SerializeField] private string clipName = "";
        [SerializeField] private string modelSearch = "";
        [SerializeField] private string clipSearch = "";
        [SerializeField] private bool compatibleOnly;
        [SerializeField] private bool playing = true;
        [SerializeField] private bool loop = true;
        [SerializeField] private float speed = 1f;
        [SerializeField] private float time;
        [SerializeField] private bool followCamera = true;
        [SerializeField] private bool inPlace = true;
        [SerializeField] private bool showGrid = true;
        [SerializeField] private bool showReference;
        [SerializeField] private float referenceHeight = 1.8f;
        [SerializeField] private Vector2 modelScroll;
        [SerializeField] private Vector2 clipScroll;

        ModelViewerStage stage;
        SearchField modelSearchField;
        SearchField clipSearchField;
        List<ModelEntry> models = new List<ModelEntry>();
        List<ClipEntry> clips = new List<ClipEntry>();
        readonly List<ModelEntry> shownModels = new List<ModelEntry>();
        readonly List<ClipEntry> shownClips = new List<ClipEntry>();
        readonly Dictionary<AnimationClip, float> matchCache = new Dictionary<AnimationClip, float>();

        GameObject modelAsset;
        ModelInfo modelInfo;
        ClipEntry currentClip;
        string infoText = "";
        string warningText = "";
        bool canConvertClip;
        int hiddenClipCount;

        bool catalogLoaded;
        bool catalogStale;
        bool filterDirty = true;
        bool poseDirty = true;
        double lastTick;

        // 눌러서 고른 것은 다음 Layout 에서 반영한다 (그리는 도중에 화면 구성이 바뀌지 않게).
        bool hasPendingModel;
        string pendingModelPath;
        bool hasPendingClip;
        string pendingClipPath;
        string pendingClipName;

        GUIStyle rowStyle;
        GUIStyle rowTagStyle;
        GUIStyle dimRowStyle;
        GUIStyle overlayStyle;
        GUIContent playIcon;
        GUIContent pauseIcon;
        readonly GUIContent rowContent = new GUIContent();

        [MenuItem(MenuPath)]
        public static void ShowWindow()
        {
            var window = GetWindow<ModelViewerWindow>("Model Viewer");
            window.minSize = new Vector2(720f, 420f);
        }

        /// <summary>프로젝트 창에서 모델이나 클립을 고른 채로 뷰어를 연다.</summary>
        [MenuItem(AssetMenuPath)]
        static void OpenSelected()
        {
            var window = GetWindow<ModelViewerWindow>("Model Viewer");
            window.minSize = new Vector2(720f, 420f);
            window.TakeSelection(Selection.activeObject);
        }

        [MenuItem(AssetMenuPath, true)]
        static bool OpenSelectedValidate()
        {
            Object selected = Selection.activeObject;
            return selected != null && AssetDatabase.Contains(selected) && (selected is GameObject || selected is AnimationClip);
        }

        /// <summary>다른 에디터 코드에서 모델을 지정해 뷰어를 여는 진입점.</summary>
        public static void Open(GameObject model, AnimationClip clip = null)
        {
            var window = GetWindow<ModelViewerWindow>("Model Viewer");
            window.minSize = new Vector2(720f, 420f);
            if (model != null) window.TakeSelection(model);
            if (clip != null) window.TakeSelection(clip);
        }

        void OnEnable()
        {
            modelSearchField = new SearchField();
            clipSearchField = new SearchField();
            stage = new ModelViewerStage();
            stage.SetInPlace(inPlace);
            catalogLoaded = false;
            filterDirty = true;
            poseDirty = true;
            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.projectChanged += OnProjectChanged;
            // 창을 다시 열었거나 스크립트가 다시 컴파일된 뒤: 보던 모델과 클립을 되살린다.
            hasPendingModel = !string.IsNullOrEmpty(modelPath);
            pendingModelPath = modelPath;
            hasPendingClip = !string.IsNullOrEmpty(clipPath);
            pendingClipPath = clipPath;
            pendingClipName = clipName;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.projectChanged -= OnProjectChanged;
            if (stage != null)
            {
                stage.Dispose();
                stage = null;
            }
        }

        void OnProjectChanged()
        {
            catalogStale = true;
            Repaint();
        }

        void OnFocus()
        {
            if (catalogStale) ReloadCatalog();
        }

        void OnSelectionChange()
        {
            if (followSelection) TakeSelection(Selection.activeObject);
        }

        /// <summary>프로젝트 창에서 고른 것이 모델이나 클립이면 뷰어에 올린다.</summary>
        void TakeSelection(Object selected)
        {
            if (selected == null || !AssetDatabase.Contains(selected)) return;
            string path = AssetDatabase.GetAssetPath(selected);
            if (selected is AnimationClip clip)
            {
                QueueClip(path, clip.name);
            }
            else if (selected is GameObject)
            {
                // "모델@동작.fbx" 처럼 이름에 @ 가 든 파일은 애니메이션만 담은 파일로 보고 클립을 올린다 (Unity 의 이름 규칙).
                if (System.IO.Path.GetFileName(path).Contains("@"))
                {
                    var found = new List<ClipEntry>();
                    ModelViewerCatalog.AddClipsAtPath(found, path);
                    if (found.Count > 0)
                    {
                        QueueClip(path, found[0].name);
                        Repaint();
                        return;
                    }
                }
                // 하위 물체를 골랐어도 그 파일의 뿌리 모델을 올린다.
                QueueModel(path);
            }
            Repaint();
        }

        void QueueModel(string path)
        {
            hasPendingModel = true;
            pendingModelPath = path;
        }

        void QueueClip(string path, string name)
        {
            hasPendingClip = true;
            pendingClipPath = path;
            pendingClipName = name;
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - lastTick);
            lastTick = now;
            if (!playing || currentClip == null || currentClip.clip == null || modelAsset == null) return;
            float length = Mathf.Max(currentClip.clip.length, 0.0001f);
            time += Mathf.Min(delta, 0.1f) * speed;
            if (time > length)
            {
                if (loop) time %= length;
                else { time = length; playing = false; }
            }
            poseDirty = true;
            Repaint();
        }

        // ------------------------------------------------------------------ 목록과 선택

        void ReloadCatalog()
        {
            searchFolder = ModelViewerCatalog.ValidFolder(searchFolder);
            models = ModelViewerCatalog.FindModels(searchFolder, includePrefabs);
            clips = ModelViewerCatalog.FindClips(searchFolder);
            catalogLoaded = true;
            catalogStale = false;
            filterDirty = true;
            matchCache.Clear();
            // 다시 임포트되면 클립 물체가 바뀔 수 있으므로 경로와 이름으로 다시 잡는다.
            if (currentClip != null) QueueClip(currentClip.path, currentClip.name);
            if (modelAsset == null && !string.IsNullOrEmpty(modelPath)) QueueModel(modelPath);
        }

        void ApplyPending()
        {
            if (hasPendingModel)
            {
                hasPendingModel = false;
                SetModel(pendingModelPath);
            }
            if (hasPendingClip)
            {
                hasPendingClip = false;
                SetClip(pendingClipPath, pendingClipName);
            }
        }

        void SetModel(string path)
        {
            GameObject asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            modelAsset = asset;
            modelPath = asset != null ? path : "";
            matchCache.Clear();
            stage.SetModel(asset, false);
            modelInfo = asset != null && stage.Instance != null ? ModelViewerCatalog.Analyze(stage.Instance, modelPath) : null;
            filterDirty = true;
            poseDirty = true;
            RebuildInfo();
        }

        void SetClip(string path, string name)
        {
            currentClip = null;
            if (!string.IsNullOrEmpty(path))
            {
                foreach (ClipEntry entry in clips)
                {
                    if (entry.path == path && entry.name == name) { currentClip = entry; break; }
                }
                if (currentClip == null)
                {
                    // 찾는 폴더 밖의 클립을 직접 끌어다 놓은 경우.
                    var extra = new List<ClipEntry>();
                    ModelViewerCatalog.AddClipsAtPath(extra, path);
                    foreach (ClipEntry entry in extra)
                    {
                        if (entry.name == name) { currentClip = entry; break; }
                    }
                    if (currentClip == null && extra.Count > 0) currentClip = extra[0];
                }
            }
            clipPath = currentClip != null ? currentClip.path : "";
            clipName = currentClip != null ? currentClip.name : "";
            time = 0f;
            // 클립을 바꾸면 앞 클립이 남긴 자세를 지우고 시작한다.
            if (stage.HasModel) stage.ResetPose();
            poseDirty = true;
            RebuildInfo();
        }

        float MatchRatio(ClipEntry entry)
        {
            if (stage == null || stage.Instance == null || entry.clip == null) return 0f;
            if (!matchCache.TryGetValue(entry.clip, out float ratio))
            {
                ratio = ModelViewerCatalog.PathMatchRatio(entry.clip, stage.Instance.transform);
                matchCache[entry.clip] = ratio;
            }
            return ratio;
        }

        /// <summary>이 클립이 지금 모델에서 재생될 것으로 보이는가.</summary>
        bool IsCompatible(ClipEntry entry)
        {
            if (modelInfo == null) return true;
            if (entry.kind == ClipKind.Humanoid) return modelInfo.isHumanoid;
            return MatchRatio(entry) >= CompatibleRatio;
        }

        void RebuildFilter()
        {
            filterDirty = false;
            shownModels.Clear();
            string ms = modelSearch.Trim().ToLowerInvariant();
            foreach (ModelEntry entry in models)
            {
                if (ms.Length == 0 || entry.name.ToLowerInvariant().Contains(ms) || entry.path.ToLowerInvariant().Contains(ms))
                    shownModels.Add(entry);
            }

            shownClips.Clear();
            hiddenClipCount = 0;
            string cs = clipSearch.Trim().ToLowerInvariant();
            // 지금 모델 파일 안에 든 클립을 맨 위에 둔다.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (ClipEntry entry in clips)
                {
                    bool own = modelAsset != null && entry.path == modelPath;
                    if (own != (pass == 0)) continue;
                    if (cs.Length > 0 && !entry.name.ToLowerInvariant().Contains(cs) && !entry.path.ToLowerInvariant().Contains(cs)) continue;
                    if (compatibleOnly && modelAsset != null && !IsCompatible(entry)) { hiddenClipCount++; continue; }
                    shownClips.Add(entry);
                }
            }
        }

        void RebuildInfo()
        {
            var info = new StringBuilder();
            var warn = new StringBuilder();
            canConvertClip = false;
            if (modelAsset == null || modelInfo == null)
            {
                info.Append("왼쪽 목록에서 모델을 고르거나, 프로젝트 창에서 모델을 선택하세요.");
            }
            else
            {
                info.Append(modelPath).Append('\n').Append(ModelViewerCatalog.Describe(modelInfo));
                if (modelInfo.renderers == 0) warn.Append("그릴 메쉬가 없습니다.\n");
                if (Mathf.Abs(modelInfo.minY) > 0.02f)
                    warn.Append("발바닥이 바닥(y=0)에서 ").Append(modelInfo.minY.ToString("0.00")).Append(" m 떨어져 있습니다.\n");
                Vector3 s = modelInfo.rootScale;
                if (Mathf.Abs(s.x - 1f) > 0.001f || Mathf.Abs(s.y - 1f) > 0.001f || Mathf.Abs(s.z - 1f) > 0.001f)
                    warn.Append("뿌리의 크기 값이 1이 아닙니다: ").Append(s.ToString("0.###")).Append('\n');
            }

            if (currentClip != null && currentClip.clip != null)
            {
                AnimationClip clip = currentClip.clip;
                info.Append("\n\n").Append(currentClip.path).Append('\n');
                info.Append("클립  ").Append(currentClip.name).Append(" · ").Append(KindLabel(currentClip.kind))
                    .Append(" · ").Append(clip.length.ToString("0.00")).Append("초 · ")
                    .Append(clip.frameRate.ToString("0")).Append(" fps")
                    .Append(clip.isLooping ? " · 반복 설정됨" : "");
                if (modelInfo != null)
                {
                    if (currentClip.kind == ClipKind.Humanoid)
                    {
                        if (!modelInfo.isHumanoid)
                            warn.Append("Humanoid 클립인데 이 모델에는 유효한 Humanoid 아바타가 없어 움직이지 않습니다.\n");
                    }
                    else
                    {
                        float ratio = MatchRatio(currentClip);
                        info.Append('\n').Append("뼈 경로 일치  ").Append(Mathf.RoundToInt(ratio * 100f)).Append('%');
                        if (ratio < CompatibleRatio)
                        {
                            warn.Append("이 클립의 뼈 이름이 모델과 거의 맞지 않아 움직이지 않습니다.\n");
                            if (modelInfo.isHumanoid && currentClip.fromModelFile && AssetImporter.GetAtPath(currentClip.path) is ModelImporter)
                            {
                                warn.Append("Mixamo 처럼 사람 뼈대로 만든 클립이면, 클립 파일을 Humanoid 로 다시 임포트하면 재생됩니다.\n");
                                canConvertClip = true;
                            }
                        }
                    }
                }
            }
            infoText = info.ToString();
            warningText = warn.ToString().TrimEnd('\n');
        }

        static string KindLabel(ClipKind kind)
        {
            switch (kind)
            {
                case ClipKind.Humanoid: return "Humanoid";
                case ClipKind.Generic: return "Generic";
                default: return "Legacy";
            }
        }

        /// <summary>클립이 든 모델 파일의 임포트 설정을 Humanoid 로 바꾼다. 사용자가 단추를 눌렀을 때만 부른다.</summary>
        void ConvertClipFileToHumanoid()
        {
            if (currentClip == null) return;
            string path = currentClip.path;
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
            bool ok = EditorUtility.DisplayDialog("Model Viewer",
                path + "\n\n이 파일의 임포트 설정(Rig > Animation Type)을 Humanoid 로 바꾸고 다시 임포트합니다.",
                "바꾸기", "취소");
            if (!ok) return;
            string name = currentClip.name;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            ReloadCatalog();
            QueueClip(path, name);
        }

        // ------------------------------------------------------------------ 그리기

        void EnsureStyles()
        {
            if (rowStyle != null) return;
            rowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(6, 4, 1, 1), clipping = TextClipping.Clip };
            dimRowStyle = new GUIStyle(rowStyle);
            dimRowStyle.normal.textColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            rowTagStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 6, 0, 0) };
            overlayStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft };
            overlayStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            playIcon = EditorGUIUtility.IconContent("PlayButton");
            pauseIcon = EditorGUIUtility.IconContent("PauseButton");
        }

        void OnGUI()
        {
            EnsureStyles();
            if (stage == null)
            {
                stage = new ModelViewerStage();
                stage.SetInPlace(inPlace);
            }
            if (Event.current.type == EventType.Layout)
            {
                if (!catalogLoaded) ReloadCatalog();
                ApplyPending();
                if (filterDirty) RebuildFilter();
            }

            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftWidth)))
                {
                    DrawModelList();
                    DrawClipList();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawPreview();
                    DrawPlayback();
                    DrawInfo();
                }
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(catalogStale ? "새로 고침 *" : "새로 고침", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    ReloadCatalog();
                GUILayout.Label("찾을 폴더", EditorStyles.miniLabel, GUILayout.Width(52f));
                EditorGUI.BeginChangeCheck();
                string folder = EditorGUILayout.DelayedTextField(searchFolder, EditorStyles.toolbarTextField, GUILayout.MinWidth(120f));
                if (GUILayout.Button("고르기", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                {
                    string picked = EditorUtility.OpenFolderPanel("찾을 폴더", Application.dataPath, "");
                    string root = Application.dataPath.Replace('\\', '/');
                    picked = string.IsNullOrEmpty(picked) ? "" : picked.Replace('\\', '/');
                    if (picked.StartsWith(root))
                    {
                        searchFolder = "Assets" + picked.Substring(root.Length);
                        ReloadCatalog();
                    }
                    GUIUtility.ExitGUI();
                }
                bool prefabs = GUILayout.Toggle(includePrefabs, "프리팹 포함", EditorStyles.toolbarButton, GUILayout.Width(76f));
                if (EditorGUI.EndChangeCheck())
                {
                    searchFolder = folder;
                    includePrefabs = prefabs;
                    ReloadCatalog();
                }
                GUILayout.FlexibleSpace();
                followSelection = GUILayout.Toggle(followSelection,
                    new GUIContent("프로젝트 창 선택 따라가기", "프로젝트 창에서 모델이나 클립을 고르면 바로 여기에 올린다."),
                    EditorStyles.toolbarButton, GUILayout.Width(150f));
            }
        }

        void DrawModelList()
        {
            GUILayout.Label("모델 (" + shownModels.Count + ")", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var dropped = (GameObject)EditorGUILayout.ObjectField(modelAsset, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                string path = dropped != null ? AssetDatabase.GetAssetPath(dropped) : "";
                QueueModel(path);
            }
            EditorGUI.BeginChangeCheck();
            modelSearch = modelSearchField.OnGUI(modelSearch);
            if (EditorGUI.EndChangeCheck()) filterDirty = true;

            modelScroll = EditorGUILayout.BeginScrollView(modelScroll, EditorStyles.helpBox, GUILayout.MinHeight(90f));
            Rect area = GUILayoutUtility.GetRect(0f, shownModels.Count * RowHeight, GUILayout.ExpandWidth(true));
            int first = Mathf.Max(0, Mathf.FloorToInt(modelScroll.y / RowHeight) - 1);
            int last = Mathf.Min(shownModels.Count - 1, first + Mathf.CeilToInt(position.height / RowHeight) + 2);
            for (int i = first; i <= last; i++)
            {
                ModelEntry entry = shownModels[i];
                var row = new Rect(area.x, area.y + i * RowHeight, area.width, RowHeight);
                bool selected = entry.path == modelPath;
                if (DrawRow(row, entry.name, entry.isPrefab ? "프리팹" : "", entry.path, selected, false))
                    QueueModel(entry.path);
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawClipList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("애니메이션 (" + shownClips.Count + ")", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                compatibleOnly = GUILayout.Toggle(compatibleOnly,
                    new GUIContent("맞는 것만", "지금 모델에서 재생될 것으로 보이는 클립만 보인다. 끄면 맞지 않는 클립은 흐리게 보인다."));
                if (EditorGUI.EndChangeCheck()) filterDirty = true;
            }
            EditorGUI.BeginChangeCheck();
            var dropped = (AnimationClip)EditorGUILayout.ObjectField(currentClip != null ? currentClip.clip : null, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (dropped != null) QueueClip(AssetDatabase.GetAssetPath(dropped), dropped.name);
                else QueueClip("", "");
            }
            EditorGUI.BeginChangeCheck();
            clipSearch = clipSearchField.OnGUI(clipSearch);
            if (EditorGUI.EndChangeCheck()) filterDirty = true;

            clipScroll = EditorGUILayout.BeginScrollView(clipScroll, EditorStyles.helpBox, GUILayout.MinHeight(90f));
            int count = shownClips.Count + 1;
            Rect area = GUILayoutUtility.GetRect(0f, count * RowHeight, GUILayout.ExpandWidth(true));
            int first = Mathf.Max(0, Mathf.FloorToInt(clipScroll.y / RowHeight) - 1);
            int last = Mathf.Min(count - 1, first + Mathf.CeilToInt(position.height / RowHeight) + 2);
            for (int i = first; i <= last; i++)
            {
                var row = new Rect(area.x, area.y + i * RowHeight, area.width, RowHeight);
                if (i == 0)
                {
                    if (DrawRow(row, "(없음 · 기본 자세)", "", "클립을 입히지 않은 모델의 원래 자세", currentClip == null, false))
                        QueueClip("", "");
                    continue;
                }
                ClipEntry entry = shownClips[i - 1];
                if (entry.clip == null) continue;
                bool selected = currentClip != null && entry.path == clipPath && entry.name == clipName;
                bool dim = modelAsset != null && !compatibleOnly && !IsCompatible(entry);
                string tag = KindLabel(entry.kind).Substring(0, 1) + "  " + entry.clip.length.ToString("0.0") + "s";
                if (DrawRow(row, entry.name, tag, entry.path, selected, dim))
                    QueueClip(entry.path, entry.name);
            }
            EditorGUILayout.EndScrollView();
            if (hiddenClipCount > 0)
                GUILayout.Label("모델에 맞지 않아 숨긴 클립 " + hiddenClipCount + "개", EditorStyles.miniLabel);
        }

        /// <summary>목록의 한 줄을 그린다. 눌렸으면 true.</summary>
        bool DrawRow(Rect row, string label, string tag, string tooltip, bool selected, bool dim)
        {
            Event e = Event.current;
            if (e.type == EventType.Repaint && selected) EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.90f, 0.45f));
            rowContent.text = label;
            rowContent.tooltip = tooltip;
            GUI.Label(new Rect(row.x, row.y, row.width - 64f, row.height), rowContent, dim ? dimRowStyle : rowStyle);
            if (tag.Length > 0) GUI.Label(new Rect(row.xMax - 70f, row.y, 70f, row.height), tag, rowTagStyle);
            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
            {
                e.Use();
                Repaint();
                return true;
            }
            return false;
        }

        void DrawPreview()
        {
            Rect rect = GUILayoutUtility.GetRect(100f, 100f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (stage.HandleInput(rect)) Repaint();
            if (Event.current.type != EventType.Repaint) return;

            stage.SetHelpers(showGrid, showReference, referenceHeight);
            if (poseDirty)
            {
                poseDirty = false;
                if (currentClip != null && currentClip.clip != null) stage.Sample(currentClip.clip, time);
            }
            stage.Render(rect, followCamera);
            var hint = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, 16f);
            GUI.Label(hint, "왼쪽 끌기: 돌리기 · 오른쪽 끌기: 옮기기 · 휠: 당기기", overlayStyle);
            if (modelInfo != null)
            {
                var size = new Rect(rect.x + 6f, rect.yMax - 20f, rect.width - 12f, 16f);
                GUI.Label(size, "높이 " + modelInfo.size.y.ToString("0.00") + " m · 바닥 격자 한 칸 = 1 m", overlayStyle);
            }
        }

        void DrawPlayback()
        {
            bool hasClip = currentClip != null && currentClip.clip != null;
            float length = hasClip ? Mathf.Max(currentClip.clip.length, 0.0001f) : 1f;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(!hasClip))
                {
                    if (GUILayout.Button(playing ? pauseIcon : playIcon, EditorStyles.toolbarButton, GUILayout.Width(32f)))
                    {
                        playing = !playing;
                        if (playing && !loop && time >= length) time = 0f;
                    }
                    EditorGUI.BeginChangeCheck();
                    float t = GUILayout.HorizontalSlider(Mathf.Min(time, length), 0f, length, GUILayout.MinWidth(80f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        time = t;
                        poseDirty = true;
                    }
                    GUILayout.Label(time.ToString("0.00") + " / " + length.ToString("0.00") + "초", EditorStyles.miniLabel, GUILayout.Width(86f));
                    loop = GUILayout.Toggle(loop, "반복", EditorStyles.toolbarButton, GUILayout.Width(40f));
                    GUILayout.Label("속도", EditorStyles.miniLabel, GUILayout.Width(26f));
                    speed = GUILayout.HorizontalSlider(speed, 0.1f, 2f, GUILayout.Width(70f));
                    GUILayout.Label(speed.ToString("0.0") + "x", EditorStyles.miniLabel, GUILayout.Width(30f));
                }
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(modelAsset == null))
                {
                    if (GUILayout.Button("앞", EditorStyles.toolbarButton, GUILayout.Width(30f))) stage.LookFrom(0f);
                    if (GUILayout.Button("옆", EditorStyles.toolbarButton, GUILayout.Width(30f))) stage.LookFrom(90f);
                    if (GUILayout.Button("뒤", EditorStyles.toolbarButton, GUILayout.Width(30f))) stage.LookFrom(180f);
                    if (GUILayout.Button("전체 보기", EditorStyles.toolbarButton, GUILayout.Width(62f))) stage.Frame();
                }
                GUILayout.Space(8f);
                EditorGUI.BeginChangeCheck();
                inPlace = GUILayout.Toggle(inPlace,
                    new GUIContent("제자리", "켜면 제자리에서 재생한다. 끄면 클립에 든 이동(루트 모션)대로 앞으로 나아간다."), EditorStyles.toolbarButton, GUILayout.Width(46f));
                if (EditorGUI.EndChangeCheck())
                {
                    stage.SetInPlace(inPlace);
                    poseDirty = true;
                }
                followCamera = GUILayout.Toggle(followCamera,
                    new GUIContent("따라가기", "앞으로 나아가는 클립에서 카메라가 모델을 따라간다."), EditorStyles.toolbarButton, GUILayout.Width(58f));
                showGrid = GUILayout.Toggle(showGrid, "바닥 격자", EditorStyles.toolbarButton, GUILayout.Width(62f));
                showReference = GUILayout.Toggle(showReference,
                    new GUIContent("키 기준", "모델 옆에 사람 키만 한 기둥을 세워 크기를 견준다."), EditorStyles.toolbarButton, GUILayout.Width(52f));
                using (new EditorGUI.DisabledScope(!showReference))
                {
                    referenceHeight = Mathf.Clamp(EditorGUILayout.FloatField(referenceHeight, EditorStyles.toolbarTextField, GUILayout.Width(40f)), 0.1f, 20f);
                    GUILayout.Label("m", EditorStyles.miniLabel, GUILayout.Width(14f));
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(modelAsset == null))
                {
                    if (GUILayout.Button(new GUIContent("그림 저장", "지금 보이는 화면을 PNG 파일로 저장한다."), EditorStyles.toolbarButton, GUILayout.Width(62f)))
                    {
                        SaveScreenshot();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        /// <summary>지금 보이는 무대를 PNG 로 저장한다. 저장할 곳은 사용자가 고른다.</summary>
        void SaveScreenshot()
        {
            string defaultName = modelAsset != null ? modelAsset.name : "model";
            if (currentClip != null) defaultName += "_" + currentClip.name;
            string path = EditorUtility.SaveFilePanel("그림 저장", "", defaultName + ".png", "png");
            if (string.IsNullOrEmpty(path)) return;
            Texture2D shot = stage.Capture(1280, 960, followCamera);
            ModelViewerCapture.SavePng(shot, path);
            DestroyImmediate(shot);
            ShowNotification(new GUIContent("저장했습니다"));
        }

        void DrawInfo()
        {
            EditorGUILayout.HelpBox(infoText, MessageType.None);
            if (warningText.Length > 0) EditorGUILayout.HelpBox(warningText, MessageType.Warning);
            if (canConvertClip)
            {
                if (GUILayout.Button("이 클립 파일을 Humanoid 로 다시 임포트"))
                {
                    ConvertClipFileToHumanoid();
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
