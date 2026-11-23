using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    /// <summary>유도표시의 종류. 둘 다 보이면 민간인은 안전지대용을 먼저 따른다.</summary>
    public enum GuideMarkerKind { Exit, SafeZone }

    /// <summary>
    /// 유도표시(유도 스티커). 플레이어가 바닥에 붙이는 화살표.
    /// 민간인은 보이는 유도표시로 걸어간 뒤, 화살표 방향 쪽의 가까운 유도표시로 이어서 이동한다.
    /// </summary>
    public class GuideMarker : MonoBehaviour
    {
        public static readonly List<GuideMarker> All = new List<GuideMarker>();

        public GuideMarkerKind kind;

        /// <summary>화살표가 가리키는 수평 방향 (월드, 단위 벡터).</summary>
        public Vector3 Direction { get; private set; }
        /// <summary>민간인이 걸어가는 바닥 위 지점.</summary>
        public Vector3 NavPoint { get; private set; }
        /// <summary>보이는지 판단할 때 쓰는 지점 (바닥 위 1m).</summary>
        public Vector3 SightPoint => NavPoint + Vector3.up;

        /// <summary>설치 위치 계산 결과.</summary>
        public struct Placement
        {
            public Vector3 point, normal, direction, navPoint;
            public Quaternion Rotation => Quaternion.LookRotation(direction, normal);
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>
        /// 바라보는 곳의 바닥에 붙일 자리를 구한다. 화살표는 보는 방향을 가리킨다.
        ///  - 조준선이 벽이나 물건에 닿으면 그 앞 바닥, 아무것도 닿지 않으면 maxDistance 앞의 바닥을 고른다.
        ///  - 걸을 수 있는 바닥(NavMesh)의 가장 가까운 점으로 맞춘다. 상자나 책상 위, 바닥 밖은 받지 않는다.
        /// feetY 는 플레이어가 서 있는 바닥의 높이, fallbackForward 는 똑바로 아래를 볼 때 쓸 방향,
        /// mask 는 판정선에 걸릴 레이어 (플레이어 자신의 몸은 빼고 넘긴다).
        /// </summary>
        public static bool TryGetFloorPlacement(Vector3 origin, Vector3 viewForward, Vector3 fallbackForward, float feetY,
                                                float maxDistance, int mask, out Placement p)
        {
            p = default;
            Vector3 probe = Physics.Raycast(origin, viewForward, out var hit, maxDistance, mask, QueryTriggerInteraction.Ignore)
                ? hit.point + hit.normal * 0.35f
                : origin + viewForward * maxDistance;

            // 플레이어가 서 있는 높이의 걸을 수 있는 바닥에서 가장 가까운 점
            if (!NavMesh.SamplePosition(new Vector3(probe.x, feetY, probe.z), out var nav, 1.2f, NavMesh.AllAreas)) return false;
            if (Mathf.Abs(nav.position.y - feetY) > 0.4f) return false;

            // 그 점 바로 아래의 바닥 면을 찾아 정확한 높이에 붙인다
            if (!Physics.Raycast(nav.position + Vector3.up * 0.5f, Vector3.down, out var floor, 1.2f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (floor.normal.y < 0.7f) return false;

            var dir = Vector3.ProjectOnPlane(viewForward, Vector3.up);
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.ProjectOnPlane(fallbackForward, Vector3.up);
            if (dir.sqrMagnitude < 1e-4f) return false;

            p.point = floor.point;
            p.normal = Vector3.up;
            p.direction = dir.normalized;
            p.navPoint = nav.position;
            return true;
        }

        public static GuideMarker Place(GuideMarkerKind kind, Placement p)
        {
            var go = BuildVisual(kind, false);
            go.name = $"GuideMarker_{kind}";
            go.transform.SetPositionAndRotation(p.point + p.normal * 0.02f, p.Rotation);
            var m = go.AddComponent<GuideMarker>();
            m.kind = kind;
            m.Direction = p.direction;
            m.NavPoint = p.navPoint;
            return m;
        }

        public static Color KindColor(GuideMarkerKind kind) =>
            kind == GuideMarkerKind.Exit ? new Color(0.2f, 1f, 0.35f) : new Color(0.25f, 0.6f, 1f);

        /// <summary>화살표 모양. 로컬 +Z 가 화살표 방향, +Y 가 붙는 면의 바깥쪽. ghost 는 설치 전 미리보기용.</summary>
        public static GameObject BuildVisual(GuideMarkerKind kind, bool ghost)
        {
            var root = new GameObject(ghost ? "GuideMarkerPreview" : "GuideMarker");
            var c = KindColor(kind);
            if (ghost) c = Color.Lerp(c, Color.white, 0.5f);
            var mat = ProtoVisuals.Lit(c, true);
            ProtoVisuals.Primitive(PrimitiveType.Cube, "Shaft", root.transform, new Vector3(0f, 0.01f, -0.08f), new Vector3(0.14f, 0.02f, 0.5f), mat);
            var l = ProtoVisuals.Primitive(PrimitiveType.Cube, "HeadL", root.transform, new Vector3(-0.1f, 0.01f, 0.18f), new Vector3(0.14f, 0.02f, 0.34f), mat);
            l.transform.localRotation = Quaternion.Euler(0f, 40f, 0f);
            var r = ProtoVisuals.Primitive(PrimitiveType.Cube, "HeadR", root.transform, new Vector3(0.1f, 0.01f, 0.18f), new Vector3(0.14f, 0.02f, 0.34f), mat);
            r.transform.localRotation = Quaternion.Euler(0f, -40f, 0f);
            return root;
        }
    }
}
