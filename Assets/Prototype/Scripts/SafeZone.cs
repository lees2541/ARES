using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace DungeonProto
{
    /// <summary>
    /// 안전지대. 플레이어가 설치하는 원형 구역으로, 프로토타입에서는 적이 들어오지 못하게만 한다.
    /// 들어온 민간인은 스스로 나가지 않는다.
    /// 적을 막는 방법: 구역을 NavMesh 의 별도 영역(NavArea)으로 굽고, 적의 길찾기에서 그 영역을 뺀다.
    /// </summary>
    public class SafeZone : MonoBehaviour
    {
        /// <summary>안전지대에 쓰는 NavMesh 영역 번호. 프로젝트 설정을 건드리지 않도록 이름 없는 사용자 영역을 쓴다.</summary>
        public const int NavArea = 3;

        public static readonly List<SafeZone> All = new List<SafeZone>();

        public float radius = 3f;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>p 가 원 안인지 (높이는 무시).</summary>
        public bool Contains(Vector3 p)
        {
            float dx = p.x - transform.position.x, dz = p.z - transform.position.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>p 가 적이 다닐 수 없게 구운 사각 영역 안인지. 원에 외접하는 정사각형이다.</summary>
        public bool ContainsNavBlock(Vector3 p)
        {
            return Mathf.Abs(p.x - transform.position.x) <= radius && Mathf.Abs(p.z - transform.position.z) <= radius;
        }

        public static SafeZone FindContaining(Vector3 p)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Contains(p)) return All[i];
            return null;
        }

        public static bool InAnyNavBlock(Vector3 p)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].ContainsNavBlock(p)) return true;
            return false;
        }

        /// <summary>구역 안쪽의 무작위 지점. 들어온 민간인이 입구를 막지 않도록 안쪽에 자리 잡게 한다.</summary>
        public Vector3 RandomPointInside()
        {
            var o = Random.insideUnitCircle * (radius * 0.6f);
            return transform.position + new Vector3(o.x, 0f, o.y);
        }

        public static SafeZone Place(Vector3 center, float radius)
        {
            var go = new GameObject("SafeZone");
            go.transform.position = center;
            var zone = go.AddComponent<SafeZone>();
            zone.radius = radius;

            var color = new Color(0.2f, 0.45f, 1f);
            ProtoVisuals.Primitive(PrimitiveType.Cylinder, "Floor", go.transform, new Vector3(0f, 0.02f, 0f),
                new Vector3(radius * 2f, 0.01f, radius * 2f), ProtoVisuals.Lit(color * 0.5f, true));
            var ring = RangeIndicator.Create("Ring");
            ring.transform.SetParent(go.transform, true);
            ring.Show(center, radius, color);

            var volume = go.AddComponent<NavMeshModifierVolume>();
            volume.center = new Vector3(0f, 1f, 0f);
            volume.size = new Vector3(radius * 2f, 3f, radius * 2f);
            volume.area = NavArea;

            var director = FindAnyObjectByType<EnemyDirector>();
            if (director != null) director.RefreshNavMesh();
            return zone;
        }
    }
}
