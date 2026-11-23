using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    /// <summary>
    /// 숨을 곳 (캐비닛). 맵에 드문드문 놓여 있고, 플레이어가 사서 설치할 수도 있다.
    ///  - 민간인은 적이 보고 있지 않을 때만 들어갈 수 있다. 들어가면 완전히 안전하다.
    ///  - 적이 그 방에서 나갔을 때만 스스로 나온다.
    ///  - 안에 누가 있는지는 플레이어가 직접 열어 봐야 안다. 열면 보이기만 하고, 직접 데리고 나와야 한다.
    /// 로컬 +Z 쪽이 문이다.
    /// </summary>
    public class HidingSpot : MonoBehaviour
    {
        public static readonly List<HidingSpot> All = new List<HidingSpot>();

        public const float Width = 1f, Height = 2f, Depth = 0.8f;

        [SerializeField] Transform door; // 경첩. 이걸 돌려 문을 연다

        readonly List<CivilianAI> occupants = new List<CivilianAI>();
        readonly List<CivilianAI> reserved = new List<CivilianAI>();
        PrototypePlayer opener;
        DungeonGenerator dungeon;
        Rect roomRect;
        bool roomSearched, hasRoom;
        Vector3 entryPoint;
        bool hasEntry;

        public bool IsOpen { get; private set; }
        public int OccupantCount => occupants.Count;
        public CivilianAI FirstOccupant => occupants.Count > 0 ? occupants[0] : null;
        /// <summary>플레이어가 조준할 때 기준이 되는 지점.</summary>
        public Vector3 AimPoint => transform.position + Vector3.up * 1.1f;
        /// <summary>숨은 민간인이 서 있는 자리.</summary>
        public Vector3 InsidePoint => transform.position;

        int Capacity => PrototypeSettings.Active.hiding.capacity;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // ───────────────────────────────── 만들기

        /// <summary>캐비닛을 만든다. 맵 생성기와 플레이어 설치가 함께 쓴다.</summary>
        public static HidingSpot Build(Transform parent, Vector3 localPos, float yaw, Material body, Material doorMaterial)
        {
            var go = new GameObject("HidingSpot");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // 길찾기의 장애물이자 플레이어가 조준하는 대상
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, Height * 0.5f, 0f);
            col.size = new Vector3(Width, Height, Depth);

            // 문을 열면 안이 보이도록 판 다섯 장으로 속이 빈 상자를 만든다
            const float t = 0.05f;
            Part("Back", go.transform, new Vector3(0f, Height * 0.5f, -Depth * 0.5f + t * 0.5f), new Vector3(Width, Height, t), body);
            Part("Left", go.transform, new Vector3(-Width * 0.5f + t * 0.5f, Height * 0.5f, 0f), new Vector3(t, Height, Depth), body);
            Part("Right", go.transform, new Vector3(Width * 0.5f - t * 0.5f, Height * 0.5f, 0f), new Vector3(t, Height, Depth), body);
            Part("Top", go.transform, new Vector3(0f, Height - t * 0.5f, 0f), new Vector3(Width, t, Depth), body);
            Part("Bottom", go.transform, new Vector3(0f, t * 0.5f, 0f), new Vector3(Width, t, Depth), body);

            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(go.transform, false);
            hinge.localPosition = new Vector3(-Width * 0.5f, 0f, Depth * 0.5f);
            Part("Door", hinge, new Vector3(Width * 0.5f, Height * 0.5f, t * 0.5f), new Vector3(Width, Height, t), doorMaterial);

            var spot = go.AddComponent<HidingSpot>();
            spot.door = hinge;
            return spot;
        }

        static void Part(string name, Transform parent, Vector3 localPos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// <summary>플레이어가 플레이 중에 설치한다. 캐비닛이 길을 막으므로 NavMesh 를 갱신한다.</summary>
        public static HidingSpot Create(Vector3 floorPos, Quaternion rotation)
        {
            var spot = Build(null, floorPos, rotation.eulerAngles.y,
                ProtoVisuals.Lit(new Color(0.23f, 0.27f, 0.34f)), ProtoVisuals.Lit(new Color(0.33f, 0.4f, 0.5f)));
            var director = FindAnyObjectByType<EnemyDirector>();
            if (director != null) director.RefreshNavMesh();
            return spot;
        }

        // ───────────────────────────────── 민간인이 쓰는 것

        /// <summary>c 가 들어갈 자리가 있는지 (c 자신의 예약과 입실은 빼고 센다).</summary>
        public bool HasRoomFor(CivilianAI c)
        {
            if (occupants.Contains(c)) return true;
            int taken = occupants.Count;
            for (int i = 0; i < reserved.Count; i++)
                if (reserved[i] != null && reserved[i] != c) taken++;
            return taken < Capacity;
        }

        /// <summary>가는 동안 다른 민간인이 자리를 차지하지 않도록 맡아 둔다.</summary>
        public void Reserve(CivilianAI c)
        {
            if (!reserved.Contains(c)) reserved.Add(c);
        }

        public void Unreserve(CivilianAI c) => reserved.Remove(c);

        public void Enter(CivilianAI c)
        {
            reserved.Remove(c);
            if (!occupants.Contains(c)) occupants.Add(c);
        }

        public void Leave(CivilianAI c)
        {
            occupants.Remove(c);
            reserved.Remove(c);
        }

        /// <summary>문 앞의 걸을 수 있는 지점. 민간인이 들어가고 나오는 자리다.</summary>
        public Vector3 EntryPoint
        {
            get
            {
                if (!hasEntry)
                {
                    var front = transform.position + transform.forward * (Depth * 0.5f + 0.55f);
                    entryPoint = NavMesh.SamplePosition(front, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : front;
                    hasEntry = true;
                }
                return entryPoint;
            }
        }

        /// <summary>이 숨을 곳이 있는 방 안에 적이 있는지. 숨은 민간인은 적이 방에서 나가야 스스로 나온다.</summary>
        public bool EnemyInRoom()
        {
            FindRoom();
            var enemies = EnemyAI.All;
            for (int i = 0; i < enemies.Count; i++)
            {
                var p = enemies[i].transform.position;
                if (hasRoom)
                {
                    var local = dungeon.transform.InverseTransformPoint(p);
                    if (roomRect.Contains(new Vector2(local.x, local.z))) return true;
                }
                else if ((p - transform.position).sqrMagnitude < 8f * 8f) return true; // 방을 모르면 8m 안
            }
            return false;
        }

        /// <summary>radius 안에 적이 있는지 (높이는 무시).</summary>
        public bool EnemyNear(float radius)
        {
            var enemies = EnemyAI.All;
            for (int i = 0; i < enemies.Count; i++)
            {
                var d = enemies[i].transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude < radius * radius) return true;
            }
            return false;
        }

        void FindRoom()
        {
            if (roomSearched) return;
            roomSearched = true;
            dungeon = FindAnyObjectByType<DungeonGenerator>();
            if (dungeon == null || dungeon.rooms == null) return;
            var local = dungeon.transform.InverseTransformPoint(transform.position);
            var p = new Vector2(local.x, local.z);
            float cs = dungeon.cellSize;
            foreach (var r in dungeon.rooms)
            {
                var rect = new Rect(r.rect.x * cs, r.rect.y * cs, r.rect.width * cs, r.rect.height * cs);
                if (!rect.Contains(p)) continue;
                roomRect = rect;
                hasRoom = true;
                return;
            }
        }

        // ───────────────────────────────── 플레이어가 쓰는 것

        /// <summary>문을 열거나 닫는다. 열려 있는 동안에만 안의 민간인이 보인다.</summary>
        public void SetOpen(bool open, PrototypePlayer by)
        {
            IsOpen = open;
            opener = open ? by : null;
            if (door != null) door.localRotation = open ? Quaternion.Euler(0f, -110f, 0f) : Quaternion.identity;
            for (int i = occupants.Count - 1; i >= 0; i--)
            {
                if (occupants[i] == null) { occupants.RemoveAt(i); continue; }
                occupants[i].SetShown(open);
            }
        }

        void Update()
        {
            // 열어 둔 채 멀어지면 닫힌다
            if (!IsOpen || opener == null) return;
            float limit = PrototypeSettings.Active.hiding.autoCloseDistance;
            if ((opener.transform.position - transform.position).sqrMagnitude > limit * limit) SetOpen(false, null);
        }
    }
}
