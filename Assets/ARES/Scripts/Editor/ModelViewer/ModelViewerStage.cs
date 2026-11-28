using System;
using UnityEditor;
using UnityEngine;

namespace ARES.EditorTools
{
    /// <summary>
    /// 모델 뷰어의 무대. 열린 씬과 분리된 미리보기 공간에 모델을 세우고, 클립의 한 시점을 입혀 그림으로 그린다.
    /// 창(<see cref="ModelViewerWindow"/>)이 만들고, 창이 닫힐 때 <see cref="Dispose"/>로 치운다.
    /// </summary>
    internal sealed class ModelViewerStage : IDisposable
    {
        const float MinDistance = 0.2f;
        const float MaxDistance = 200f;
        const int GridHalfLines = 10;
        static readonly int ControlHash = "ARES.ModelViewerStage".GetHashCode();

        PreviewRenderUtility preview;
        GameObject sourceAsset;
        GameObject instance;
        Animator animator;
        Transform followAnchor;
        Vector3 anchorRestPosition;

        GameObject gridObject;
        Mesh gridMesh;
        Material lineMaterial;
        GameObject referenceObject;
        Material referenceMaterial;

        float yaw = 205f;
        float pitch = 12f;
        float distance = 4f;
        Vector3 pivot = new Vector3(0f, 0.9f, 0f);
        Vector3 followOffset;
        int dragButton = -1;
        bool inPlace = true;

        /// <summary>지금 세워 둔 모델. 없으면 null.</summary>
        public GameObject Instance => instance;

        /// <summary>쉬는 자세일 때의 모델 범위.</summary>
        public Bounds ModelBounds { get; private set; }

        public bool HasModel => instance != null;

        /// <summary>무대에 모델을 세운다. null 이면 비운다. 카메라는 keepCamera 가 아니면 모델에 맞춘다.</summary>
        public void SetModel(GameObject asset, bool keepCamera)
        {
            EnsurePreview();
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            instance = null;
            animator = null;
            followAnchor = null;
            followOffset = Vector3.zero;
            sourceAsset = asset;
            if (asset == null) return;

            instance = UnityEngine.Object.Instantiate(asset);
            instance.name = asset.name;
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            preview.AddSingleGO(instance);

            animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.applyRootMotion = !inPlace;
            ModelBounds = MeasureBounds(instance);
            followAnchor = FindFollowAnchor();
            anchorRestPosition = followAnchor != null ? followAnchor.position : Vector3.zero;
            PlaceReference();
            if (!keepCamera) Frame();
        }

        /// <summary>
        /// 제자리 재생을 켜고 끈다. 끄면 클립에 든 뿌리 이동(루트 모션)대로 모델이 앞으로 나아간다.
        /// Animator 가 있는 모델에만 듣는다. 바꾼 뒤에는 모델을 다시 세운다.
        /// </summary>
        public void SetInPlace(bool value)
        {
            if (inPlace == value) return;
            inPlace = value;
            ResetPose();
        }

        /// <summary>모델을 쉬는 자세로 되돌린다 (다시 세운다).</summary>
        public void ResetPose()
        {
            if (sourceAsset != null) SetModel(sourceAsset, true);
        }

        /// <summary>
        /// 클립의 한 시점을 모델에 입힌다. Humanoid 클립은 모델에 Humanoid 아바타가 있어야 움직인다.
        /// </summary>
        public void Sample(AnimationClip clip, float time)
        {
            if (instance == null || clip == null) return;
            GameObject target = animator != null ? animator.gameObject : instance;
            clip.SampleAnimation(target, time);
            followOffset = followAnchor != null ? followAnchor.position - anchorRestPosition : Vector3.zero;
            followOffset.y = 0f;
        }

        /// <summary>카메라를 모델 전체가 보이게 맞춘다.</summary>
        public void Frame()
        {
            Bounds b = ModelBounds;
            float radius = Mathf.Max(Mathf.Max(b.extents.x, b.extents.y), Mathf.Max(b.extents.z, 0.1f)) * 1.3f;
            pivot = b.center;
            // 시야각 30도의 절반(15도)에 모델이 들어오는 거리.
            distance = Mathf.Clamp(radius / Mathf.Tan(15f * Mathf.Deg2Rad), MinDistance, MaxDistance);
        }

        /// <summary>정해진 방향에서 보게 한다. 0 = 앞, 90 = 왼쪽 옆, 180 = 뒤.</summary>
        public void LookFrom(float degreesFromFront)
        {
            yaw = 180f - degreesFromFront;
            pitch = 5f;
        }

        /// <summary>바닥 격자와 키 기준을 켜고 끈다.</summary>
        public void SetHelpers(bool showGrid, bool showReference, float referenceHeight)
        {
            EnsurePreview();
            if (gridObject != null) gridObject.SetActive(showGrid);
            if (referenceObject != null)
            {
                referenceObject.SetActive(showReference && instance != null);
                float h = Mathf.Max(referenceHeight, 0.1f);
                referenceObject.transform.localScale = new Vector3(0.5f, h * 0.5f, 0.5f);
                Vector3 p = referenceObject.transform.position;
                referenceObject.transform.position = new Vector3(p.x, h * 0.5f, p.z);
            }
        }

        /// <summary>마우스로 카메라를 돌리고 옮기고 당긴다. 바뀐 것이 있으면 true.</summary>
        public bool HandleInput(Rect rect)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(ControlHash, FocusType.Passive, rect);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        dragButton = e.button;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        if (dragButton == 0 && !e.alt)
                        {
                            yaw += e.delta.x * 0.4f;
                            pitch = Mathf.Clamp(pitch + e.delta.y * 0.4f, -85f, 85f);
                        }
                        else
                        {
                            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
                            float k = distance * 0.0012f;
                            pivot += rotation * new Vector3(-e.delta.x * k, e.delta.y * k, 0f);
                        }
                        e.Use();
                        return true;
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        dragButton = -1;
                        e.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        distance = Mathf.Clamp(distance * (1f + e.delta.y * 0.05f), MinDistance, MaxDistance);
                        e.Use();
                        return true;
                    }
                    break;
            }
            return false;
        }

        /// <summary>무대를 그린다. Repaint 이벤트에서만 부른다.</summary>
        public void Render(Rect rect, bool followModel)
        {
            if (rect.width < 2f || rect.height < 2f) return;
            EnsurePreview();
            preview.BeginPreview(rect, GUIStyle.none);
            ApplyCamera(followModel);
            preview.Render(true);
            Texture texture = preview.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        /// <summary>지금 카메라로 무대를 그려 그림으로 돌려준다. 화면(OnGUI) 밖에서도 부를 수 있다. 받은 쪽이 지운다.</summary>
        public Texture2D Capture(int width, int height, bool followModel)
        {
            EnsurePreview();
            preview.BeginStaticPreview(new Rect(0f, 0f, Mathf.Max(width, 16), Mathf.Max(height, 16)));
            ApplyCamera(followModel);
            preview.Render(true);
            Texture2D raw = preview.EndStaticPreview();
            // 미리보기 도구는 화면 배율에 따라 더 크게 굽기도 한다. 부탁받은 크기로 맞춰 돌려준다.
            if (raw.width == width && raw.height == height) return raw;
            Texture2D sized = Resample(raw, width, height);
            UnityEngine.Object.DestroyImmediate(raw);
            return sized;
        }

        static Texture2D Resample(Texture2D source, int width, int height)
        {
            Color32[] src = source.GetPixels32();
            int sw = source.width, sh = source.height;
            var dst = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float fy = (y + 0.5f) * sh / height - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, sh - 1);
                int y1 = Mathf.Min(y0 + 1, sh - 1);
                float ty = Mathf.Clamp01(fy - y0);
                for (int x = 0; x < width; x++)
                {
                    float fx = (x + 0.5f) * sw / width - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, sw - 1);
                    int x1 = Mathf.Min(x0 + 1, sw - 1);
                    float tx = Mathf.Clamp01(fx - x0);
                    Color32 a = Color32.Lerp(src[y0 * sw + x0], src[y0 * sw + x1], tx);
                    Color32 b = Color32.Lerp(src[y1 * sw + x0], src[y1 * sw + x1], tx);
                    dst[y * width + x] = Color32.Lerp(a, b, ty);
                }
            }
            var result = new Texture2D(width, height, TextureFormat.RGB24, false);
            result.SetPixels32(dst);
            result.Apply();
            return result;
        }

        void ApplyCamera(bool followModel)
        {
            Camera cam = preview.camera;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 center = pivot + (followModel ? followOffset : Vector3.zero);
            cam.transform.SetPositionAndRotation(center + rotation * new Vector3(0f, 0f, -distance), rotation);
            cam.nearClipPlane = Mathf.Max(distance * 0.01f, 0.01f);
            cam.farClipPlane = distance + 400f;
            preview.lights[0].transform.rotation = rotation * Quaternion.Euler(35f, 35f, 0f);
            preview.lights[1].transform.rotation = rotation * Quaternion.Euler(-20f, -140f, 0f);
        }

        public void Dispose()
        {
            if (preview != null)
            {
                preview.Cleanup();
                preview = null;
            }
            // Cleanup 이 미리보기 공간의 물체를 함께 치운다. 직접 만든 메쉬와 머티리얼만 따로 지운다.
            instance = null;
            gridObject = null;
            referenceObject = null;
            if (gridMesh != null) UnityEngine.Object.DestroyImmediate(gridMesh);
            if (lineMaterial != null) UnityEngine.Object.DestroyImmediate(lineMaterial);
            if (referenceMaterial != null) UnityEngine.Object.DestroyImmediate(referenceMaterial);
            gridMesh = null;
            lineMaterial = null;
            referenceMaterial = null;
        }

        void EnsurePreview()
        {
            if (preview != null) return;
            preview = new PreviewRenderUtility();
            Camera cam = preview.camera;
            cam.fieldOfView = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.22f, 0.23f, 0.25f, 1f);
            preview.lights[0].intensity = 1.15f;
            preview.lights[0].color = Color.white;
            preview.lights[1].intensity = 0.55f;
            preview.lights[1].color = new Color(0.8f, 0.85f, 1f);
            preview.ambientColor = new Color(0.42f, 0.42f, 0.45f, 1f);
            BuildGrid();
            BuildReference();
        }

        void BuildGrid()
        {
            int lineCount = (GridHalfLines * 2 + 1) * 2;
            var vertices = new Vector3[lineCount * 2];
            var colors = new Color[lineCount * 2];
            var indices = new int[lineCount * 2];
            var minor = new Color(1f, 1f, 1f, 0.10f);
            var major = new Color(1f, 1f, 1f, 0.30f);
            int v = 0;
            for (int i = -GridHalfLines; i <= GridHalfLines; i++)
            {
                Color c = i == 0 ? major : minor;
                vertices[v] = new Vector3(i, 0f, -GridHalfLines); vertices[v + 1] = new Vector3(i, 0f, GridHalfLines);
                vertices[v + 2] = new Vector3(-GridHalfLines, 0f, i); vertices[v + 3] = new Vector3(GridHalfLines, 0f, i);
                for (int k = 0; k < 4; k++) { colors[v + k] = c; indices[v + k] = v + k; }
                v += 4;
            }
            gridMesh = new Mesh { name = "ModelViewerGrid", hideFlags = HideFlags.HideAndDontSave };
            gridMesh.vertices = vertices;
            gridMesh.colors = colors;
            gridMesh.SetIndices(indices, MeshTopology.Lines, 0);

            // 정점 색을 그대로 그리는 내장 셰이더. URP 에서도 그려진다.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            gridObject = new GameObject("ModelViewerGrid") { hideFlags = HideFlags.HideAndDontSave };
            gridObject.AddComponent<MeshFilter>().sharedMesh = gridMesh;
            gridObject.AddComponent<MeshRenderer>().sharedMaterial = lineMaterial;
            preview.AddSingleGO(gridObject);
        }

        void BuildReference()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            referenceMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var gray = new Color(0.55f, 0.6f, 0.68f, 1f);
            if (referenceMaterial.HasProperty("_BaseColor")) referenceMaterial.SetColor("_BaseColor", gray);
            if (referenceMaterial.HasProperty("_Color")) referenceMaterial.SetColor("_Color", gray);

            referenceObject = new GameObject("ModelViewerReference") { hideFlags = HideFlags.HideAndDontSave };
            referenceObject.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Capsule.fbx");
            referenceObject.AddComponent<MeshRenderer>().sharedMaterial = referenceMaterial;
            referenceObject.SetActive(false);
            preview.AddSingleGO(referenceObject);
        }

        /// <summary>키 기준을 모델 옆(화면에서 겹치지 않는 곳)에 둔다.</summary>
        void PlaceReference()
        {
            if (referenceObject == null) return;
            float x = ModelBounds.min.x - 0.45f;
            referenceObject.transform.position = new Vector3(x, referenceObject.transform.position.y, ModelBounds.center.z);
        }

        /// <summary>카메라가 따라갈 기준 뼈. Humanoid 는 골반, 그 밖에는 스키닝의 뿌리 뼈나 모델 뿌리.</summary>
        Transform FindFollowAnchor()
        {
            if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) return hips;
            }
            var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skinned != null && skinned.rootBone != null) return skinned.rootBone;
            return instance.transform;
        }

        static Bounds MeasureBounds(GameObject go)
        {
            bool has = false;
            var bounds = new Bounds(go.transform.position + Vector3.up * 0.5f, Vector3.one);
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (has) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; has = true; }
            }
            return bounds;
        }
    }
}
