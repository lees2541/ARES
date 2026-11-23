using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    /// <summary>유도표시의 종류. 둘 다 보이면 민간인은 안전지대용을 먼저 따른다.</summary>
    public enum GuideMarkerKind { Exit, SafeZone }

    /// <summary>
    /// 유도표시(유도 스티커). 플레이어가 바닥이나 벽에 붙이는 화살표.
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
        /// 바라보는 면(point, normal)에 붙일 수 있는지와 화살표 방향을 구한다.
        /// 화살표는 플레이어가 보는 방향을 가리킨다. 벽에서는 그 방향을 벽면에 눕힌다.
        /// </summary>
        public static bool TryGetPlacement(Vector3 point, Vector3 normal, Vector3 viewForward, out Placement p)
        {
            p = default;
            if (normal.y < -0.3f) return false; // 천장에는 붙이지 않는다

            Vector3 dir;
            if (normal.y > 0.7f)
            {
                normal = Vector3.up;
                dir = Vector3.ProjectOnPlane(viewForward, Vector3.up);
            }
            else
            {
                normal.y = 0f;
                normal.Normalize();
                dir = Vector3.ProjectOnPlane(viewForward, normal);
                dir.y = 0f;
                // 벽을 정면으로 보고 있으면 방향이 정해지지 않으므로 오른쪽으로 둔다
                if (dir.sqrMagnitude < 0.04f) dir = Vector3.Cross(Vector3.up, -normal);
            }
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();

            // 민간인이 걸어갈 수 있는 곳(NavMesh) 근처여야 한다
            if (!NavMesh.SamplePosition(point + normal * 0.5f, out var hit, 3f, NavMesh.AllAreas)) return false;

            p.point = point;
            p.normal = normal;
            p.direction = dir;
            p.navPoint = hit.position;
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
