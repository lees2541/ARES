using System.Collections.Generic;
using UnityEngine;

namespace DungeonProto
{
    /// <summary>
    /// 플레이 중에 만드는 그레이박스 표시물(도형, 선, 글자)을 위한 공용 도구.
    /// 민간인·유도표시·안전지대·범위 표시기가 함께 쓴다.
    /// </summary>
    public static class ProtoVisuals
    {
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        static readonly Dictionary<Color, Material> litCache = new Dictionary<Color, Material>();
        static readonly Dictionary<Color, Material> glowCache = new Dictionary<Color, Material>();
        static Material lineMat;
        static Font font;

        /// <summary>단색 머티리얼. glow 가 true 면 어두운 곳에서도 보이게 스스로 빛난다.</summary>
        public static Material Lit(Color c, bool glow = false)
        {
            var cache = glow ? glowCache : litCache;
            if (cache.TryGetValue(c, out var cached) && cached != null) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = "ProtoRuntime_" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor(BaseColorId, c);
            m.color = c;
            m.SetFloat("_Smoothness", 0.1f);
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor(EmissionColorId, c * 0.8f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            cache[c] = m;
            return m;
        }

        /// <summary>선(LineRenderer)용 머티리얼. 정점 색을 그대로 쓰고 양면이 보인다.</summary>
        public static Material Line()
        {
            if (lineMat != null) return lineMat;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            lineMat = new Material(shader) { name = "ProtoRuntime_Line" };
            return lineMat;
        }

        /// <summary>충돌체가 없는 기본 도형.</summary>
        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // 같은 프레임에 NavMesh 를 다시 구울 수 있으므로 충돌체는 바로 없앤다
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>월드에 놓는 글자. 글자는 자기 -Z 쪽에서 읽힌다.</summary>
        public static TextMesh WorldText(string name, Transform parent, Vector3 localPos, float characterSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            tm.fontSize = 48;
            tm.characterSize = characterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.richText = true;
            return tm;
        }
    }
}
