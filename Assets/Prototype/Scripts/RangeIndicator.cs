using UnityEngine;

namespace DungeonProto
{
    /// <summary>
    /// 바닥에 그리는 범위 원. 유도표시·안전지대·디코이를 쓰기 전에 적용 범위를 보여 준다.
    /// 굵기·선분 수·색·높이는 PrototypeSettings 의 "범위 표시기"에서 바꾼다.
    /// </summary>
    public class RangeIndicator : MonoBehaviour
    {
        LineRenderer line;
        float builtRadius = -1f;
        int builtSegments = -1;

        public static RangeIndicator Create(string name)
        {
            var go = new GameObject(name);
            // 선의 면이 바닥과 나란하도록 눕힌다 (로컬 XY 평면 → 월드 XZ 평면)
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var ind = go.AddComponent<RangeIndicator>();
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.alignment = LineAlignment.TransformZ;
            lr.sharedMaterial = ProtoVisuals.Line();
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            ind.line = lr;
            return ind;
        }

        /// <summary>center 를 중심으로 반지름 radius 인 원을 보여 준다.</summary>
        public void Show(Vector3 center, float radius, Color color)
        {
            var cfg = PrototypeSettings.Active.rangeIndicator;
            int segments = Mathf.Max(8, cfg.segments);
            if (!Mathf.Approximately(radius, builtRadius) || segments != builtSegments)
            {
                builtRadius = radius;
                builtSegments = segments;
                line.positionCount = segments;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
                }
            }
            line.widthMultiplier = cfg.lineWidth;
            line.startColor = color;
            line.endColor = color;
            transform.position = center + Vector3.up * cfg.heightOffset;
            line.enabled = true;
        }

        public void Hide()
        {
            if (line != null) line.enabled = false;
        }
    }
}
