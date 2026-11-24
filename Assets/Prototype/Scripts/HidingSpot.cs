using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    /// <summary>
    /// 숨을 곳 (철제 사물함). 맵에 드문드문 놓여 있고, 플레이어가 사서 설치할 수도 있다.
    ///  - 민간인은 적이 보고 있지 않을 때만 들어갈 수 있다. 들어가면 완전히 안전하다.
    ///  - 적이 그 방에서 나갔을 때만 스스로 나온다.
    ///  - 안에 민간인이 있으면, 플레이어가 가까이 갔을 때 문이 덜컹거리고 숨소리가 들린다 (인기척).
    ///    누가 있는지 눈으로 보려면 열어야 하고, 열면 보이기만 한다. 직접 데리고 나와야 한다.
    /// 로컬 +Z 쪽이 문이다. 양쪽으로 열리는 문 두 짝, 통풍구, 손잡이, 이름표로 장식용 가구와 구분한다.
    /// </summary>
    public class HidingSpot : MonoBehaviour
    {
        public static readonly List<HidingSpot> All = new List<HidingSpot>();

        public const float Width = 1f, Height = 2f, Depth = 0.8f;
        const float OpenAngle = 110f;

        // 색. 맵의 어두운 장식용 가구와 구분되도록 밝은 청록빛 철제로 한다
        public static readonly Color BodyColor = new Color(0.27f, 0.40f, 0.42f);
        public static readonly Color DoorColor = new Color(0.40f, 0.58f, 0.60f);
        public static readonly Color DarkColor = new Color(0.06f, 0.08f, 0.09f);
        public static readonly Color HandleColor = new Color(0.80f, 0.80f, 0.72f);
        public static readonly Color PlateColor = new Color(0.95f, 0.85f, 0.35f);
        /// <summary>이름표가 어두운 곳에서 스스로 내는 빛.</summary>
        public static Color PlateGlow => PlateColor * 0.35f;

        /// <summary>사물함을 이루는 머티리얼 묶음.</summary>
        public struct Look
        {
            public Material body, door, dark, handle, plate;

            public Look(Material body, Material door, Material dark, Material handle, Material plate)
            {
                this.body = body;
                this.door = door;
                this.dark = dark;
                this.handle = handle;
                this.plate = plate;
            }
        }

        static Look runtimeLook;

        [SerializeField] Transform door;  // 왼쪽 문의 경첩. 이걸 돌려 문을 연다
        [SerializeField] Transform door2; // 오른쪽 문의 경첩 (예전에 만든 한 짝짜리에는 없다)

        readonly List<CivilianAI> occupants = new List<CivilianAI>();
        readonly List<CivilianAI> reserved = new List<CivilianAI>();
        PrototypePlayer opener;
        DungeonGenerator dungeon;
        Rect roomRect;
        bool roomSearched, hasRoom;
        Vector3 entryPoint;
        bool hasEntry;

        // 문 움직임 · 인기척
        static PrototypePlayer nearPlayer;
        static float nextPlayerSearch;
        float doorAngle, appliedAngle;
        float rattleUntil, nextRattleAt;
        AudioSource breath;

        public bool IsOpen { get; private set; }
        public int OccupantCount => occupants.Count;
        public CivilianAI FirstOccupant => occupants.Count > 0 ? occupants[0] : null;
        /// <summary>플레이어가 조준할 때 기준이 되는 지점.</summary>
        public Vector3 AimPoint => transform.position + Vector3.up * 1.1f;
        /// <summary>숨은 민간인이 서 있는 자리.</summary>
        public Vector3 InsidePoint => transform.position;
        /// <summary>닫혀 있고 안에 민간인이 있으며, 플레이어가 인기척을 알아챌 만큼 가까이 있는지.</summary>
        public bool PresenceRevealed { get; private set; }

        int Capacity => PrototypeSettings.Active.hiding.capacity;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // ───────────────────────────────── 만들기

        /// <summary>사물함을 만든다. 맵 생성기와 플레이어 설치가 함께 쓴다.</summary>
        public static HidingSpot Build(Transform parent, Vector3 localPos, float yaw, Look look)
        {
            var go = new GameObject("HidingSpot");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // 길찾기의 장애물이자 플레이어가 조준하는 대상
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, Height * 0.5f, 0f);
            col.size = new Vector3(Width, Height, Depth);

            const float t = 0.05f;          // 판 두께
            const float plinth = 0.1f;      // 아래 받침 높이
            const float header = 0.12f;     // 문 위 머리판 높이
            float front = Depth * 0.5f;
            var root = go.transform;

            // 문을 열면 안이 보이도록 판으로 속이 빈 상자를 만든다
            Part("Back", root, new Vector3(0f, Height * 0.5f, -front + t * 0.5f), new Vector3(Width, Height, t), look.body, true);
            Part("Left", root, new Vector3(-Width * 0.5f + t * 0.5f, Height * 0.5f, 0f), new Vector3(t, Height, Depth), look.body, true);
            Part("Right", root, new Vector3(Width * 0.5f - t * 0.5f, Height * 0.5f, 0f), new Vector3(t, Height, Depth), look.body, true);
            Part("Top", root, new Vector3(0f, Height - t * 0.5f, 0f), new Vector3(Width, t, Depth), look.body, true);
            Part("Bottom", root, new Vector3(0f, t * 0.5f, 0f), new Vector3(Width, t, Depth), look.body, true);

            // 위 덮개(살짝 튀어나옴), 문 위 머리판과 이름표, 아래 받침
            Part("Cap", root, new Vector3(0f, Height + 0.015f, 0f), new Vector3(Width + 0.04f, 0.03f, Depth + 0.04f), look.door, true);
            Part("Header", root, new Vector3(0f, Height - header * 0.5f, front - t * 0.5f), new Vector3(Width, header, t), look.body, false);
            Part("NamePlate", root, new Vector3(0f, Height - header * 0.5f, front + 0.006f), new Vector3(0.24f, 0.06f, 0.012f), look.plate, false);
            Part("Plinth", root, new Vector3(0f, plinth * 0.5f, front - 0.008f), new Vector3(Width, plinth, 0.024f), look.dark, false);

            // 양쪽으로 열리는 문 두 짝. 가운데 틈으로 어두운 안쪽이 보인다
            float doorHeight = Height - header - plinth;
            float doorCenterY = plinth + doorHeight * 0.5f;
            float doorWidth = Width * 0.5f - 0.012f;

            var spot = go.AddComponent<HidingSpot>();
            spot.door = BuildDoor("DoorHingeL", root, -1f, doorWidth, doorHeight, doorCenterY, front, t, look);
            spot.door2 = BuildDoor("DoorHingeR", root, 1f, doorWidth, doorHeight, doorCenterY, front, t, look);
            return spot;
        }

        /// <summary>문 한 짝. side 가 -1 이면 왼쪽(경첩이 왼쪽 모서리), 1 이면 오른쪽.</summary>
        static Transform BuildDoor(string name, Transform root, float side, float width, float height, float centerY, float front, float t, Look look)
        {
            var hinge = new GameObject(name).transform;
            hinge.SetParent(root, false);
            hinge.localPosition = new Vector3(side * Width * 0.5f, 0f, front);

            // 경첩에서 안쪽(가운데)으로 뻗는다
            float inward = -side;
            float centerX = inward * (width * 0.5f + 0.004f);
            Part("Door", hinge, new Vector3(centerX, centerY, t * 0.5f), new Vector3(width, height, t), look.door, true);

            // 통풍구: 문 위쪽의 가로 틈 세 줄
            for (int i = 0; i < 3; i++)
                Part("Vent" + i, hinge, new Vector3(centerX, centerY + height * 0.5f - 0.2f - i * 0.06f, t + 0.004f),
                    new Vector3(width * 0.62f, 0.024f, 0.012f), look.dark, false);

            // 손잡이: 가운데 쪽 가장자리
            Part("Handle", hinge, new Vector3(inward * (width - 0.07f), 1.05f, t + 0.025f), new Vector3(0.035f, 0.26f, 0.05f), look.handle, false);
            return hinge;
        }

        static void Part(string name, Transform parent, Vector3 localPos, Vector3 size, Material m, bool castShadow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            // 작은 장식은 그림자를 드리우지 않는다
            if (!castShadow) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>플레이 중에 만드는 사물함의 머티리얼. 한 번 만들어 함께 쓴다.</summary>
        static Look RuntimeLook()
        {
            if (runtimeLook.body == null || runtimeLook.plate == null)
            {
                runtimeLook = new Look(ProtoVisuals.Lit(BodyColor), ProtoVisuals.Lit(DoorColor), ProtoVisuals.Lit(DarkColor),
                    ProtoVisuals.Lit(HandleColor), NewPlateMaterial());
            }
            return runtimeLook;
        }

        static Material NewPlateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = "ProtoRuntime_HidingSpotPlate" };
            m.SetColor(ProtoVisuals.BaseColorId, PlateColor);
            m.color = PlateColor;
            m.EnableKeyword("_EMISSION");
            m.SetColor(ProtoVisuals.EmissionColorId, PlateGlow);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        /// <summary>플레이어가 플레이 중에 설치한다. 사물함이 길을 막으므로 NavMesh 를 갱신한다.</summary>
        public static HidingSpot Create(Vector3 floorPos, Quaternion rotation)
        {
            var spot = Build(null, floorPos, rotation.eulerAngles.y, RuntimeLook());
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
            if (open != IsOpen)
                ProtoAudio.PlayAt(open ? ProtoSound.DoorOpen : ProtoSound.DoorClose, AimPoint, 0.8f, 16f);
            IsOpen = open;
            opener = open ? by : null;
            for (int i = occupants.Count - 1; i >= 0; i--)
            {
                if (occupants[i] == null) { occupants.RemoveAt(i); continue; }
                occupants[i].SetShown(open);
            }
        }

        void Update()
        {
            // 열어 둔 채 멀어지면 닫힌다
            if (IsOpen && opener != null)
            {
                float limit = PrototypeSettings.Active.hiding.autoCloseDistance;
                if ((opener.transform.position - transform.position).sqrMagnitude > limit * limit) SetOpen(false, null);
            }

            UpdatePresence();
            AnimateDoors();
        }

        /// <summary>
        /// 인기척: 닫혀 있고 안에 민간인이 있을 때 플레이어가 가까이 오면 문이 덜컹거리고 숨소리가 난다.
        /// 거리는 설정의 "인기척을 알아채는 거리"이며 0 이면 끈다.
        /// </summary>
        void UpdatePresence()
        {
            bool revealed = false;
            float closeness = 0f;
            float reveal = 0f;
            if (!IsOpen && occupants.Count > 0)
            {
                reveal = PrototypeSettings.Active.hiding.revealDistance;
                var player = reveal > 0f ? NearPlayer() : null;
                if (player != null)
                {
                    float dist = Vector3.Distance(player.transform.position, transform.position);
                    revealed = dist <= reveal;
                    closeness = 1f - Mathf.Clamp01(dist / reveal);
                }
            }
            PresenceRevealed = revealed;

            if (revealed)
            {
                if (breath == null)
                    breath = ProtoAudio.AttachLoop(ProtoSound.HiddenBreathLoop, transform, Vector3.up * 1.3f, reveal + 3f);
                if (Time.time >= nextRattleAt)
                {
                    rattleUntil = Time.time + 0.45f;
                    nextRattleAt = Time.time + Random.Range(2.2f, 4.5f);
                    ProtoAudio.PlayAt(ProtoSound.CabinetRattle, AimPoint, 0.75f, reveal + 4f, Random.Range(0.9f, 1.1f));
                }
            }
            else nextRattleAt = 0f; // 다시 다가오면 바로 덜컹거린다

            if (breath != null)
            {
                float target = revealed ? Mathf.Lerp(0.4f, 1f, closeness) * ProtoAudio.Master : 0f;
                breath.volume = Mathf.MoveTowards(breath.volume, target, Time.deltaTime * 2f);
            }
        }

        /// <summary>가장 최근에 찾은 플레이어. 없으면 가끔씩만 다시 찾는다.</summary>
        static PrototypePlayer NearPlayer()
        {
            if (nearPlayer == null && Time.time >= nextPlayerSearch)
            {
                nextPlayerSearch = Time.time + 1f;
                nearPlayer = FindAnyObjectByType<PrototypePlayer>();
            }
            return nearPlayer;
        }

        /// <summary>문을 부드럽게 여닫고, 덜컹거릴 때는 살짝 벌어졌다 닫히게 한다.</summary>
        void AnimateDoors()
        {
            doorAngle = Mathf.MoveTowards(doorAngle, IsOpen ? OpenAngle : 0f, 480f * Time.deltaTime);
            float rattle = Time.time < rattleUntil ? 2.5f + 2.5f * Mathf.Sin(Time.time * 70f) : 0f;
            float angle = Mathf.Max(doorAngle, rattle);
            if (Mathf.Approximately(angle, appliedAngle)) return;
            appliedAngle = angle;
            if (door != null) door.localRotation = Quaternion.Euler(0f, -angle, 0f);
            if (door2 != null) door2.localRotation = Quaternion.Euler(0f, angle, 0f);
        }
    }
}
