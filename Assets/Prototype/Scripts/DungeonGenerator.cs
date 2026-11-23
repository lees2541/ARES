using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Random = System.Random;

namespace DungeonProto
{
    public enum RoomKind { Entrance, Corridor, Storage, Office, Machine, Hall }

    [Serializable]
    public class RoomInfo
    {
        public int id;
        public RectInt rect;          // 셀 좌표 (x, y = 최소 셀)
        public RoomKind kind;
        public float height;
        public int depth;             // 입구로부터의 연결 거리
        public List<int> links = new List<int>();
        [NonSerialized] public List<Vector3> doorPoints = new List<Vector3>();
        [NonSerialized] public List<Rect> occupied = new List<Rect>();
    }

    /// <summary>
    /// 시설형 던전(외부 착륙지 + 복도와 방이 무작위로 이어진 내부) 그레이박스 생성기.
    /// 같은 seed 는 항상 같은 맵을 만든다.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class DungeonGenerator : MonoBehaviour
    {
        [Header("Layout")]
        public int seed = 1234;
        [Tooltip("복도를 포함한 목표 구역 수")]
        public int targetRooms = 34;
        public float cellSize = 4f;
        public Vector2Int gridMin = new Vector2Int(-12, 0);
        public Vector2Int gridMax = new Vector2Int(12, 24);
        [Range(0f, 1f)] public float loopChance = 0.15f;
        [Range(0f, 1f)] public float corridorChance = 0.7f;

        [Header("Gameplay markers")]
        public int enemySpawnCount = 3;
        [Tooltip("입구 문에서 착륙지 가장자리까지의 거리(m). 상점과 생존률 표시판은 그 사이, 입구 가까이에 놓인다.")]
        public float landingDistance = 14f;
        [Tooltip("예전 루팅 시험용 물체를 방에 놓을지. 구조 루프 프로토타입에서는 쓰지 않는다.")]
        public bool spawnLoot = false;
        [Tooltip("방마다 숨을 곳(캐비닛) 하나가 놓일 확률. 게임 설정의 \"숨을 곳\"에서 정한다.")]
        [Range(0f, 1f)] public float hidingSpotChance = 0.35f;
        public Vector2Int lootPerRoom = new Vector2Int(1, 3);

        [Header("Atmosphere")]
        [Range(0f, 1f)] public float brokenLightChance = 0.2f;
        public bool buildExterior = true;
        public bool applyAtmosphere = true;

        [HideInInspector] public List<RoomInfo> rooms = new List<RoomInfo>();

        const float WallT = 0.3f;
        const float DoorW = 2.0f;
        const float DoorH = 2.6f;
        const string RootName = "__Generated";

        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        struct Edge : IEquatable<Edge>
        {
            public Vector2Int cell; public int axis; // axis 0: cell <-> cell+right, 1: cell <-> cell+up
            public Edge(Vector2Int c, int a) { cell = c; axis = a; }
            public bool Equals(Edge o) => cell == o.cell && axis == o.axis;
            public override bool Equals(object obj) => obj is Edge e && Equals(e);
            public override int GetHashCode() => cell.GetHashCode() * 3 + axis;
            public Vector2Int Other => axis == 0 ? cell + Vector2Int.right : cell + Vector2Int.up;
        }

        static Edge MakeEdge(Vector2Int c, Vector2Int dir)
        {
            if (dir == Vector2Int.right) return new Edge(c, 0);
            if (dir == Vector2Int.up) return new Edge(c, 1);
            if (dir == Vector2Int.left) return new Edge(c + Vector2Int.left, 0);
            return new Edge(c + Vector2Int.down, 1);
        }

        Random rng;
        Dictionary<Vector2Int, int> occ;
        HashSet<Edge> doors;
        Edge exteriorDoor;
        Transform root, geoRoot, propRoot, lightRoot, markerRoot;
        Dictionary<string, Material> mats;

        // ───────────────────────────────── public API

        void Awake()
        {
            // Play 를 누르면 게임 설정의 "맵" 값으로 던전을 새로 만든다.
            // 실행 순서를 앞당겨 두었으므로 EnemyDirector 가 NavMesh 를 굽기 전에 끝난다.
            var settings = PrototypeSettings.Active;
            if (!settings.map.generateOnPlay) return;
            int useSeed = settings.map.randomSeed ? UnityEngine.Random.Range(1, 999999) : settings.map.seed;
            ApplySettings(settings, useSeed);
            Generate();
        }

        /// <summary>게임 설정의 "맵" 값을 생성기에 옮긴다. 맵 크기의 원본은 설정 에셋이고, 이 컴포넌트의 값은 마지막으로 만든 맵의 값이다.</summary>
        public void ApplySettings(PrototypeSettings settings, int useSeed)
        {
            var map = settings.map;
            seed = useSeed;
            targetRooms = map.targetRooms;
            loopChance = map.loopChance;
            corridorChance = map.corridorChance;
            // 입구 방(3x3 칸)이 들어갈 수 있도록 최소 6칸
            int cellsX = Mathf.Max(6, Mathf.RoundToInt(map.width / cellSize));
            int cellsY = Mathf.Max(6, Mathf.RoundToInt(map.depth / cellSize));
            gridMin = new Vector2Int(-(cellsX / 2), 0);
            gridMax = new Vector2Int(gridMin.x + cellsX - 1, cellsY - 1);
            enemySpawnCount = settings.enemy.count;
            hidingSpotChance = settings.hiding.mapChance;
        }

        [ContextMenu("Generate")]
        public void Generate()
        {
            Clear();
            rng = new Random(seed);
            mats = new Dictionary<string, Material>();

            for (int pass = 0; pass < 4 && rooms.Count < targetRooms; pass++)
            {
                if (pass == 0) Layout(); else Grow();
                PruneDeadEndCorridors();
            }
            AddLoops();
            ComputeDepths();

            root = new GameObject(RootName).transform;
            root.SetParent(transform, false);
            geoRoot = Group("Geometry");
            propRoot = Group("Props");
            lightRoot = Group("Lights");
            markerRoot = Group("Markers");

            BuildRooms();
            BuildWalls();
            PlaceGameplay();
            PlaceProps();
            PlaceLights();
            if (buildExterior) BuildExterior();
            if (applyAtmosphere) ApplyAtmosphere();
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            rooms = new List<RoomInfo>();
            var old = transform.Find(RootName);
            if (old == null) return;
            if (Application.isPlaying)
            {
                // Destroy 는 프레임 끝에 처리된다. 같은 프레임에 굽는 NavMesh 에 예전 맵이 잡히지 않도록 먼저 끈다
                old.gameObject.SetActive(false);
                old.name = RootName + "_Old";
                Destroy(old.gameObject);
            }
            else DestroyImmediate(old.gameObject);
        }

        // ───────────────────────────────── layout

        void Layout()
        {
            rooms = new List<RoomInfo>();
            occ = new Dictionary<Vector2Int, int>();
            doors = new HashSet<Edge>();
            AddRoom(new RectInt(-1, 0, 3, 3), RoomKind.Entrance);
            exteriorDoor = MakeEdge(new Vector2Int(0, 0), Vector2Int.down);
            Grow();
        }

        void Grow()
        {
            int attempts = 0;
            int maxAttempts = Mathf.Max(5000, targetRooms * 200); // 큰 맵은 시도 횟수도 늘린다
            while (rooms.Count < targetRooms && attempts++ < maxAttempts)
            {
                var parent = rooms[rng.Next(rooms.Count)];
                if (parent.links.Count >= (parent.kind == RoomKind.Corridor ? 3 : 4)) continue;

                var cell = new Vector2Int(parent.rect.x + rng.Next(parent.rect.width), parent.rect.y + rng.Next(parent.rect.height));
                var dir = Dirs[rng.Next(4)];
                var n = cell + dir;
                if (parent.rect.Contains(n) || occ.ContainsKey(n) || !InBounds(n)) continue;
                if (parent.kind == RoomKind.Entrance && dir == Vector2Int.down) continue;

                bool corridor = parent.kind == RoomKind.Corridor ? rng.NextDouble() < 0.3 : rng.NextDouble() < corridorChance;
                RectInt r = default; RoomKind kind = RoomKind.Corridor; bool ok = false;
                if (corridor)
                {
                    for (int len = rng.Next(2, 7); len >= 1 && !ok; len--)
                    {
                        r = CorridorRect(n, dir, len);
                        ok = Fits(r);
                    }
                }
                else
                {
                    PickRoomTemplate(out int w, out int h, out kind);
                    r = RoomRect(n, dir, w, h);
                    ok = Fits(r);
                }
                if (!ok) continue;

                var room = AddRoom(r, kind);
                Link(parent, room, MakeEdge(cell, dir));
            }
        }

        void PickRoomTemplate(out int w, out int h, out RoomKind kind)
        {
            int roll = rng.Next(100);
            if (roll < 30) { kind = RoomKind.Storage; w = 3 + rng.Next(2); h = 3 + rng.Next(2); }
            else if (roll < 60) { kind = RoomKind.Office; w = 2 + rng.Next(2); h = 2 + rng.Next(2); if (w == 2 && h == 2) w = 3; }
            else if (roll < 85) { kind = RoomKind.Machine; w = 3 + rng.Next(2); h = 3 + rng.Next(2); }
            else { kind = RoomKind.Hall; w = 4 + rng.Next(2); h = 4 + rng.Next(2); }
        }

        RectInt CorridorRect(Vector2Int n, Vector2Int dir, int len)
        {
            if (dir == Vector2Int.right) return new RectInt(n.x, n.y, len, 1);
            if (dir == Vector2Int.left) return new RectInt(n.x - len + 1, n.y, len, 1);
            if (dir == Vector2Int.up) return new RectInt(n.x, n.y, 1, len);
            return new RectInt(n.x, n.y - len + 1, 1, len);
        }

        RectInt RoomRect(Vector2Int n, Vector2Int dir, int w, int h)
        {
            if (dir == Vector2Int.right) return new RectInt(n.x, n.y - rng.Next(h), w, h);
            if (dir == Vector2Int.left) return new RectInt(n.x - w + 1, n.y - rng.Next(h), w, h);
            if (dir == Vector2Int.up) return new RectInt(n.x - rng.Next(w), n.y, w, h);
            return new RectInt(n.x - rng.Next(w), n.y - h + 1, w, h);
        }

        bool InBounds(Vector2Int c) => c.x >= gridMin.x && c.x <= gridMax.x && c.y >= gridMin.y && c.y <= gridMax.y;

        bool Fits(RectInt r)
        {
            foreach (var c in r.allPositionsWithin)
                if (!InBounds(c) || occ.ContainsKey(c)) return false;
            return true;
        }

        RoomInfo AddRoom(RectInt r, RoomKind kind)
        {
            var room = new RoomInfo { id = rooms.Count, rect = r, kind = kind, height = HeightFor(kind) };
            rooms.Add(room);
            foreach (var c in r.allPositionsWithin) occ[c] = room.id;
            return room;
        }

        static float HeightFor(RoomKind k)
        {
            switch (k)
            {
                case RoomKind.Corridor: return 3.2f;
                case RoomKind.Hall: return 6f;
                case RoomKind.Entrance: return 5f;
                default: return 4f;
            }
        }

        void Link(RoomInfo a, RoomInfo b, Edge e)
        {
            a.links.Add(b.id); b.links.Add(a.id);
            doors.Add(e);
        }

        void PruneDeadEndCorridors()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var r in rooms)
                {
                    if (r.kind != RoomKind.Corridor || r.links.Count > 1 || r.id < 0) continue;
                    RemoveRoom(r);
                    changed = true;
                }
            }
            Compact();
        }

        void RemoveRoom(RoomInfo r)
        {
            foreach (var c in r.rect.allPositionsWithin) occ.Remove(c);
            foreach (var other in r.links) rooms[other].links.Remove(r.id);
            r.links.Clear();
            doors.RemoveWhere(e => r.rect.Contains(e.cell) || r.rect.Contains(e.Other));
            r.id = -1;
        }

        void Compact()
        {
            var remap = new Dictionary<int, int>();
            var kept = new List<RoomInfo>();
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].id < 0) continue;
                remap[i] = kept.Count;
                kept.Add(rooms[i]);
            }
            foreach (var r in kept)
            {
                r.id = remap[r.id];
                for (int i = 0; i < r.links.Count; i++) r.links[i] = remap[r.links[i]];
            }
            rooms = kept;
            occ.Clear();
            foreach (var r in rooms)
                foreach (var c in r.rect.allPositionsWithin) occ[c] = r.id;
        }

        void AddLoops()
        {
            var cells = new List<Vector2Int>(occ.Keys);
            cells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y)); // 결정적 순서
            foreach (var c in cells)
            {
                for (int d = 0; d < 2; d++)
                {
                    var n = c + Dirs[d];
                    if (!occ.TryGetValue(n, out int b)) continue;
                    int a = occ[c];
                    if (a == b || rooms[a].links.Contains(b)) continue;
                    if (rng.NextDouble() >= loopChance) continue;
                    Link(rooms[a], rooms[b], MakeEdge(c, Dirs[d]));
                }
            }
        }

        void ComputeDepths()
        {
            foreach (var r in rooms) r.depth = int.MaxValue;
            var q = new Queue<int>();
            rooms[0].depth = 0; q.Enqueue(0);
            while (q.Count > 0)
            {
                var r = rooms[q.Dequeue()];
                foreach (var l in r.links)
                    if (rooms[l].depth > r.depth + 1) { rooms[l].depth = r.depth + 1; q.Enqueue(l); }
            }
        }

        // ───────────────────────────────── geometry

        Vector3 CellMin(Vector2Int c) => new Vector3(c.x * cellSize, 0f, c.y * cellSize);

        Rect RoomWorldRect(RoomInfo r) =>
            new Rect(r.rect.x * cellSize, r.rect.y * cellSize, r.rect.width * cellSize, r.rect.height * cellSize);

        void BuildRooms()
        {
            foreach (var r in rooms)
            {
                var wr = RoomWorldRect(r);
                var group = new GameObject($"Room_{r.id:00}_{r.kind}").transform;
                group.SetParent(geoRoot, false);
                bool corr = r.kind == RoomKind.Corridor;
                Box("Floor", group, new Vector3(wr.center.x, -0.1f, wr.center.y), new Vector3(wr.width, 0.2f, wr.height),
                    M(corr ? "CorridorFloor" : "Floor"));
                Box("Ceiling", group, new Vector3(wr.center.x, r.height + 0.1f, wr.center.y), new Vector3(wr.width, 0.2f, wr.height),
                    M("Ceiling"));
            }
        }

        float H(int room) => room >= 0 ? rooms[room].height : 0f;

        void BuildWalls()
        {
            var edges = new HashSet<Edge>();
            var ordered = new List<Edge>();
            foreach (var kv in occ)
                foreach (var d in Dirs)
                {
                    var n = kv.Key + d;
                    if (occ.TryGetValue(n, out int other) && other == kv.Value) continue;
                    var e = MakeEdge(kv.Key, d);
                    if (edges.Add(e)) ordered.Add(e);
                }

            var wallGroup = new GameObject("Walls").transform;
            wallGroup.SetParent(geoRoot, false);

            foreach (var e in ordered)
            {
                int ra = occ.TryGetValue(e.cell, out int a) ? a : -1;
                int rb = occ.TryGetValue(e.Other, out int b) ? b : -1;
                float h = Mathf.Max(H(ra), H(rb));
                bool isExterior = e.Equals(exteriorDoor);
                bool isDoor = doors.Contains(e) || isExterior;

                if (isDoor && ra >= 0 && rb >= 0 &&
                    rooms[ra].kind == RoomKind.Corridor && rooms[rb].kind == RoomKind.Corridor)
                    continue; // 복도끼리는 완전히 트여 있음

                if (isDoor) BuildDoorWall(wallGroup, e, h, ra, rb, isExterior);
                else BuildSolidWall(wallGroup, e, h);
            }
        }

        void EdgeFrame(Edge e, out Vector3 mid, out Vector3 along)
        {
            var c = CellMin(e.cell);
            if (e.axis == 0) { mid = c + new Vector3(cellSize, 0f, cellSize * 0.5f); along = Vector3.forward; }
            else { mid = c + new Vector3(cellSize * 0.5f, 0f, cellSize); along = Vector3.right; }
        }

        Vector3 WallSize(Vector3 along, float len, float h, float t) =>
            along == Vector3.right ? new Vector3(len, h, t) : new Vector3(t, h, len);

        void BuildSolidWall(Transform parent, Edge e, float h)
        {
            EdgeFrame(e, out var mid, out var along);
            Box("Wall", parent, mid + Vector3.up * (h * 0.5f), WallSize(along, cellSize + WallT, h, WallT), M("Wall"));
        }

        void BuildDoorWall(Transform parent, Edge e, float h, int ra, int rb, bool exterior)
        {
            EdgeFrame(e, out var mid, out var along);
            float dw = exterior ? 3f : DoorW;
            float dh = exterior ? 3.2f : DoorH;
            float seg = (cellSize - dw) * 0.5f + WallT * 0.5f;
            float off = dw * 0.5f + seg * 0.5f;
            var wm = M("Wall");
            Box("DoorWall", parent, mid + along * off + Vector3.up * (h * 0.5f), WallSize(along, seg, h, WallT), wm);
            Box("DoorWall", parent, mid - along * off + Vector3.up * (h * 0.5f), WallSize(along, seg, h, WallT), wm);
            Box("Lintel", parent, mid + Vector3.up * (dh + (h - dh) * 0.5f), WallSize(along, dw, h - dh, WallT), wm);

            var fm = M(exterior ? "ExitFrame" : "DoorFrame");
            float ft = 0.15f, fd = WallT + 0.12f;
            Box("Frame", parent, mid + along * (dw * 0.5f + ft * 0.5f) + Vector3.up * (dh * 0.5f), WallSize(along, ft, dh, fd), fm, false);
            Box("Frame", parent, mid - along * (dw * 0.5f + ft * 0.5f) + Vector3.up * (dh * 0.5f), WallSize(along, ft, dh, fd), fm, false);
            Box("Frame", parent, mid + Vector3.up * (dh + ft * 0.5f), WallSize(along, dw + ft * 2f, ft, fd), fm, false);

            if (ra >= 0) rooms[ra].doorPoints.Add(mid);
            if (rb >= 0) rooms[rb].doorPoints.Add(mid);

            if (exterior)
            {
                Box("ExitSign_In", parent, mid + Vector3.up * (dh + 0.45f) + Vector3.forward * 0.2f, new Vector3(1.2f, 0.35f, 0.08f), M("ExitSign"), false);
                Box("ExitSign_Out", parent, mid + Vector3.up * (dh + 0.45f) - Vector3.forward * 0.2f, new Vector3(1.6f, 0.45f, 0.08f), M("ExitSign"), false);
            }
        }

        // ───────────────────────────────── gameplay markers

        void PlaceGameplay()
        {
            // 플레이어 스폰: 외부 착륙지 앞 (외부 미생성 시 입구 안)
            var spawnPos = buildExterior ? new Vector3(0f, 0.05f, -landingDistance + 2f) : new Vector3(0f, 0.05f, cellSize * 1.5f);
            Marker("PlayerSpawn", SpawnKind.PlayerSpawn, spawnPos, 0f, 0);

            // 탈출구: 입구 방 안, 바깥 문 바로 안쪽. 민간인을 여기까지 데려오면 구출이다
            {
                EdgeFrame(exteriorDoor, out var doorMid, out _);
                var p = doorMid + Vector3.forward * 2.6f;
                var go = Marker("Exit", SpawnKind.Extraction, p, 0f, 0);
                Box("Pad", go.transform, Vector3.up * 0.03f, new Vector3(3f, 0.06f, 3f), M("Extraction"), false, true);
                var l = AddLight(go.transform, Vector3.up * 2.5f, new Color(0.3f, 1f, 0.45f), 6f, 2f);
                l.name = "ExitLight";
                rooms[0].occupied.Add(new Rect(p.x - 2f, p.z - 2f, 4f, 4f));
            }

            // 적 스폰(벤트): 입구에서 먼 구역 위주, 복도 포함
            var enemyRooms = rooms.FindAll(r => r.kind != RoomKind.Entrance);
            enemyRooms.Sort((a, b) => b.depth.CompareTo(a.depth));
            int placed = 0;
            for (int i = 0; i < enemyRooms.Count && placed < enemySpawnCount; i += 2)
            {
                var r = enemyRooms[i];
                if (!TryFindSpot(r, new Vector2(1.4f, 1.4f), 0.3f, out var p)) continue;
                var go = Marker($"EnemyVent_{placed++}", SpawnKind.EnemySpawn, p, 0f, 0);
                Box("Vent", go.transform, Vector3.up * 0.03f, new Vector3(1.2f, 0.06f, 1.2f), M("Vent"), false, true);
            }
        }

        // ───────────────────────────────── props & loot

        void PlaceProps()
        {
            int lootId = 0;
            foreach (var r in rooms)
            {
                var group = new GameObject($"Props_{r.id:00}_{r.kind}").transform;
                group.SetParent(propRoot, false);
                var wr = RoomWorldRect(r);
                int area = r.rect.width * r.rect.height;

                // 숨을 곳은 다른 물건보다 먼저 놓아 문 앞 자리를 확보한다
                if (r.kind != RoomKind.Corridor && r.kind != RoomKind.Entrance && rng.NextDouble() < hidingSpotChance)
                    PlaceHidingSpot(r, group);

                switch (r.kind)
                {
                    case RoomKind.Entrance:
                        Box("ReceptionDesk", group, new Vector3(wr.center.x + 2.5f, 0.55f, wr.center.y + 2f), new Vector3(3f, 1.1f, 0.8f), M("Prop"));
                        PlaceCrates(r, group, 2);
                        break;
                    case RoomKind.Storage:
                        for (int i = 0; i < area / 2 + 1; i++) PlaceAlongWall(r, group, "Shelf", new Vector2(0.7f, 2f + (float)rng.NextDouble() * 1.5f), 2.4f, M("Shelf"));
                        PlaceCrates(r, group, 2 + rng.Next(3));
                        break;
                    case RoomKind.Office:
                        for (int i = 0; i < area / 2 + 1; i++)
                        {
                            if (!TryFindSpot(r, new Vector2(1.8f, 1.6f), 0.5f, out var p)) continue;
                            Box("Desk", group, p + Vector3.up * 0.375f, new Vector3(1.6f, 0.75f, 0.8f), M("Prop"));
                            Box("Chair", group, p + new Vector3(0f, 0.45f, -0.75f), new Vector3(0.5f, 0.9f, 0.5f), M("PropDark"));
                        }
                        for (int i = 0; i < 2; i++) PlaceAlongWall(r, group, "Cabinet", new Vector2(0.6f, 1.2f), 1.4f, M("PropDark"));
                        break;
                    case RoomKind.Machine:
                        for (int i = 0; i < area / 4 + 1; i++)
                        {
                            float s = 1.6f + (float)rng.NextDouble() * 1.2f;
                            if (!TryFindSpot(r, new Vector2(s, s), 0.8f, out var p)) continue;
                            float mh = 1.5f + (float)rng.NextDouble() * 1.5f;
                            Box("Machine", group, p + Vector3.up * (mh * 0.5f), new Vector3(s, mh, s), M("Machine"));
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            if (!TryFindSpot(r, new Vector2(0.6f, 0.6f), 0.2f, out var p, true)) continue;
                            Cyl("Pipe", group, p + Vector3.up * (r.height * 0.5f), 0.35f, r.height, M("Pipe"));
                        }
                        break;
                    case RoomKind.Hall:
                        for (int x = 1; x < r.rect.width; x++)
                            for (int y = 1; y < r.rect.height; y++)
                            {
                                if ((x + y) % 2 != 0) continue;
                                var p = new Vector3((r.rect.x + x) * cellSize, 0f, (r.rect.y + y) * cellSize);
                                if (NearDoor(r, p, 2.5f)) continue;
                                Cyl("Pillar", group, p + Vector3.up * (r.height * 0.5f), 0.9f, r.height, M("Pillar"));
                                r.occupied.Add(new Rect(p.x - 0.8f, p.z - 0.8f, 1.6f, 1.6f));
                            }
                        PlaceCrates(r, group, 3 + rng.Next(4));
                        break;
                    case RoomKind.Corridor:
                        if (rng.NextDouble() < 0.35) PlaceCrates(r, group, 1);
                        break;
                }

                if (!spawnLoot || r.kind == RoomKind.Corridor || r.kind == RoomKind.Entrance) continue;

                int lootCount = rng.Next(lootPerRoom.x, lootPerRoom.y + 1);
                if (r.links.Count == 1) lootCount++;      // 막다른 방 보너스
                if (r.kind == RoomKind.Hall) lootCount++;
                for (int i = 0; i < lootCount; i++)
                {
                    float s = 0.3f + (float)rng.NextDouble() * 0.45f;
                    if (!TryFindSpot(r, new Vector2(s + 0.3f, s + 0.3f), 0.2f, out var p)) continue;
                    int value = 20 + rng.Next(130) + r.depth * 10;
                    var go = Marker($"Loot_{lootId++:000}", SpawnKind.Loot, p + Vector3.up * (s * 0.5f + 0.01f), (float)rng.NextDouble() * 360f, value);
                    go.transform.SetParent(group, true);
                    var vis = Box("Item", go.transform, Vector3.zero, Vector3.one * s, M("Loot"), true, true);
                    UnityEngine.Object.DestroyImmediate(vis.GetComponent<Collider>());
                    var col = go.AddComponent<BoxCollider>();
                    col.size = Vector3.one * s;
                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = 2f + s * 10f;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                }
            }
        }

        /// <summary>숨을 곳(캐비닛)을 벽에 붙여 놓는다. 문은 방 안쪽을 향하고, 문 앞은 비워 둔다.</summary>
        void PlaceHidingSpot(RoomInfo r, Transform group)
        {
            var wr = RoomWorldRect(r);
            float w = HidingSpot.Width, d = HidingSpot.Depth, front = 1.2f;
            float inset = WallT * 0.5f + d * 0.5f + 0.03f;
            for (int tries = 0; tries < 14; tries++)
            {
                int side = rng.Next(4);
                Vector3 p; float yaw; Rect rect;
                if (side < 2)
                {
                    float z = Mathf.Lerp(wr.yMin + w * 0.5f + 0.4f, wr.yMax - w * 0.5f - 0.4f, (float)rng.NextDouble());
                    bool left = side == 0;
                    p = new Vector3(left ? wr.xMin + inset : wr.xMax - inset, 0f, z);
                    yaw = left ? 90f : -90f;
                    rect = left ? new Rect(p.x - d * 0.5f, z - w * 0.5f, d + front, w)
                                : new Rect(p.x - d * 0.5f - front, z - w * 0.5f, d + front, w);
                }
                else
                {
                    float x = Mathf.Lerp(wr.xMin + w * 0.5f + 0.4f, wr.xMax - w * 0.5f - 0.4f, (float)rng.NextDouble());
                    bool bottom = side == 2;
                    p = new Vector3(x, 0f, bottom ? wr.yMin + inset : wr.yMax - inset);
                    yaw = bottom ? 0f : 180f;
                    rect = bottom ? new Rect(x - w * 0.5f, p.z - d * 0.5f, w, d + front)
                                  : new Rect(x - w * 0.5f, p.z - d * 0.5f - front, w, d + front);
                }
                if (!SpotFree(r, rect, 0.3f) || NearDoor(r, p, 2.6f)) continue;
                r.occupied.Add(rect);
                HidingSpot.Build(group, p, yaw, M("Cabinet"), M("CabinetDoor"));
                return;
            }
        }

        void PlaceCrates(RoomInfo r, Transform group, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float s = 0.8f + (float)rng.NextDouble() * 0.5f;
                if (!TryFindSpot(r, new Vector2(s, s), 0.4f, out var p, true)) continue;
                var c = Box("Crate", group, p + Vector3.up * (s * 0.5f), Vector3.one * s, M("Crate"));
                c.transform.localRotation = Quaternion.Euler(0f, rng.Next(-15, 15), 0f);
                if (rng.NextDouble() < 0.4)
                {
                    float s2 = s * 0.7f;
                    Box("Crate", group, p + Vector3.up * (s + s2 * 0.5f), Vector3.one * s2, M("Crate"));
                }
            }
        }

        void PlaceAlongWall(RoomInfo r, Transform group, string name, Vector2 depthLen, float height, Material m)
        {
            var wr = RoomWorldRect(r);
            float inset = WallT * 0.5f + depthLen.x * 0.5f + 0.05f;
            for (int tries = 0; tries < 12; tries++)
            {
                int side = rng.Next(4);
                bool alongX = side >= 2;
                float len = depthLen.y;
                Vector3 p; Vector2 size;
                if (!alongX)
                {
                    float z = Mathf.Lerp(wr.yMin + len * 0.5f + 0.3f, wr.yMax - len * 0.5f - 0.3f, (float)rng.NextDouble());
                    p = new Vector3(side == 0 ? wr.xMin + inset : wr.xMax - inset, 0f, z);
                    size = new Vector2(depthLen.x, len);
                }
                else
                {
                    float x = Mathf.Lerp(wr.xMin + len * 0.5f + 0.3f, wr.xMax - len * 0.5f - 0.3f, (float)rng.NextDouble());
                    p = new Vector3(x, 0f, side == 2 ? wr.yMin + inset : wr.yMax - inset);
                    size = new Vector2(len, depthLen.x);
                }
                var rect = new Rect(p.x - size.x * 0.5f, p.z - size.y * 0.5f, size.x, size.y);
                if (!SpotFree(r, rect, 0.5f) || NearDoor(r, p, 2.2f + len * 0.5f)) continue;
                r.occupied.Add(rect);
                Box(name, group, p + Vector3.up * (height * 0.5f), new Vector3(size.x, height, size.y), m);
                return;
            }
        }

        bool TryFindSpot(RoomInfo r, Vector2 size, float gap, out Vector3 pos, bool preferEdges = false)
        {
            var wr = RoomWorldRect(r);
            float m = WallT * 0.5f + 0.2f;
            for (int tries = 0; tries < 25; tries++)
            {
                float tx = (float)rng.NextDouble(), tz = (float)rng.NextDouble();
                if (preferEdges && rng.NextDouble() < 0.7)
                {
                    if (rng.NextDouble() < 0.5) tx = tx < 0.5f ? 0f : 1f; else tz = tz < 0.5f ? 0f : 1f;
                }
                float x = Mathf.Lerp(wr.xMin + m + size.x * 0.5f, wr.xMax - m - size.x * 0.5f, tx);
                float z = Mathf.Lerp(wr.yMin + m + size.y * 0.5f, wr.yMax - m - size.y * 0.5f, tz);
                var rect = new Rect(x - size.x * 0.5f, z - size.y * 0.5f, size.x, size.y);
                var p = new Vector3(x, 0f, z);
                if (!SpotFree(r, rect, gap) || NearDoor(r, p, 1.8f + Mathf.Max(size.x, size.y) * 0.5f)) continue;
                r.occupied.Add(rect);
                pos = p;
                return true;
            }
            pos = default;
            return false;
        }

        static bool SpotFree(RoomInfo r, Rect rect, float gap)
        {
            var grown = new Rect(rect.x - gap, rect.y - gap, rect.width + gap * 2f, rect.height + gap * 2f);
            foreach (var o in r.occupied) if (o.Overlaps(grown)) return false;
            return true;
        }

        static bool NearDoor(RoomInfo r, Vector3 p, float dist)
        {
            foreach (var d in r.doorPoints)
                if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(d.x, d.z)) < dist) return true;
            return false;
        }

        // ───────────────────────────────── lights

        void PlaceLights()
        {
            var warm = new Color(1f, 0.86f, 0.66f);
            var cool = new Color(0.8f, 0.9f, 1f);
            foreach (var r in rooms)
            {
                var group = new GameObject($"Lights_{r.id:00}").transform;
                group.SetParent(lightRoot, false);
                if (r.kind == RoomKind.Corridor)
                {
                    int i = 0;
                    foreach (var c in r.rect.allPositionsWithin)
                    {
                        if (i++ % 2 != 0) continue;
                        var p = CellMin(c) + new Vector3(cellSize * 0.5f, r.height - 0.05f, cellSize * 0.5f);
                        Fixture(group, p, new Vector3(0.3f, 0.08f, 1.2f), cool, 7f, 1.6f);
                    }
                    continue;
                }
                int nx = Mathf.CeilToInt(r.rect.width / 3f), ny = Mathf.CeilToInt(r.rect.height / 3f);
                var wr = RoomWorldRect(r);
                for (int x = 0; x < nx; x++)
                    for (int y = 0; y < ny; y++)
                    {
                        var p = new Vector3(wr.xMin + wr.width * (x + 0.5f) / nx, r.height - 0.05f, wr.yMin + wr.height * (y + 0.5f) / ny);
                        float range = Mathf.Max(wr.width / nx, wr.height / ny) * 1.3f + r.height;
                        Fixture(group, p, new Vector3(0.7f, 0.08f, 0.7f), warm, range, r.kind == RoomKind.Hall ? 2.6f : 1.9f);
                    }
            }
        }

        void Fixture(Transform parent, Vector3 p, Vector3 size, Color c, float range, float intensity)
        {
            bool broken = rng.NextDouble() < brokenLightChance;
            Box(broken ? "Fixture_Broken" : "Fixture", parent, p, size, M(broken ? "FixtureOff" : "FixtureOn"), false);
            if (broken) return;
            AddLight(parent, p + Vector3.down * 0.2f, c, range, intensity);
        }

        Light AddLight(Transform parent, Vector3 localPos, Color c, float range, float intensity)
        {
            var go = new GameObject("PointLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        // ───────────────────────────────── exterior

        void BuildExterior()
        {
            var ext = new GameObject("Exterior").transform;
            ext.SetParent(root, false);

            // 착륙지의 입구 쪽 가장자리가 문에서 landingDistance 만큼 떨어지도록 통째로 옮긴다 (예전 배치는 35m)
            float padCenter = -landingDistance - 12f;
            float dz = padCenter + 47f;

            // 바닥은 건물과 착륙지를 모두 덮도록 맵 크기에 맞춘다
            var b = BuildingBounds();
            float gxMin = Mathf.Min(b.xMin, -30f) - 50f, gxMax = Mathf.Max(b.xMax, 30f) + 50f;
            float gzMin = padCenter - 52f, gzMax = b.yMax + 50f;
            Box("Ground", ext, new Vector3((gxMin + gxMax) * 0.5f, -0.52f, (gzMin + gzMax) * 0.5f),
                new Vector3(gxMax - gxMin, 1f, gzMax - gzMin), M("Ground"));
            Box("Path", ext, new Vector3(0f, -0.06f, -(landingDistance + 1f) * 0.5f), new Vector3(3.2f, 0.1f, landingDistance + 1f), M("Path"), false);

            // 착륙지 + 수송선(플레이스홀더)
            Box("LandingPad", ext, new Vector3(0f, 0.0f, padCenter), new Vector3(18f, 0.3f, 24f), M("Pad"));
            var ship = new GameObject("DropShip").transform;
            ship.SetParent(ext, false);
            Box("Hull", ship, new Vector3(0f, 3.6f, -48f + dz), new Vector3(10f, 5f, 14f), M("Ship"));
            Box("Cockpit", ship, new Vector3(0f, 4.4f, -56.5f + dz), new Vector3(6f, 3f, 3f), M("Ship"));
            Box("CockpitGlass", ship, new Vector3(0f, 4.8f, -58.05f + dz), new Vector3(4.5f, 1.2f, 0.1f), M("Glass"), false);
            float rampAngle = Mathf.Atan2(1.1f, 4.6f) * Mathf.Rad2Deg;
            var ramp = Box("Ramp", ship, new Vector3(0f, 0.6f, -38.7f + dz), new Vector3(3.5f, 0.2f, 4.8f), M("ShipDark"));
            ramp.transform.localRotation = Quaternion.Euler(rampAngle, 0f, 0f);
            foreach (var lx in new[] { -4f, 4f })
                foreach (var lz in new[] { -53f, -43f })
                    Cyl("Leg", ship, new Vector3(lx, 0.6f, lz + dz), 0.5f, 1.2f, M("ShipDark"));
            AddLight(ship, new Vector3(0f, 2.8f, -40.6f + dz), new Color(1f, 0.95f, 0.8f), 10f, 2.5f).name = "RampLight";

            // 입구 외부 조명
            AddLight(ext, new Vector3(0f, 4.2f, -1.5f), new Color(1f, 0.8f, 0.5f), 9f, 2.2f).name = "EntranceLight";

            // 상점: 입구로 가는 길 오른쪽. 여기서만 물품을 살 수 있다
            var shop = new GameObject("Shop");
            shop.transform.SetParent(ext, false);
            shop.transform.localPosition = new Vector3(4.6f, 0f, -5.5f);
            shop.AddComponent<ShopStation>();
            Box("Counter", shop.transform, new Vector3(0f, 0.55f, 0f), new Vector3(2.6f, 1.1f, 1.1f), M("Crate"));
            Box("Back", shop.transform, new Vector3(0.9f, 1.2f, 0f), new Vector3(0.5f, 2.4f, 1.6f), M("ShipDark"));
            Box("Roof", shop.transform, new Vector3(0f, 2.45f, 0f), new Vector3(3f, 0.12f, 2f), M("ShipDark"));
            AddLight(shop.transform, new Vector3(-0.6f, 2.1f, 0f), new Color(1f, 0.85f, 0.5f), 7f, 2.2f).name = "ShopLight";

            // 생존률 표시판: 입구 문 왼쪽, 길 건너 상점 맞은편. 글자는 착륙지 쪽을 향한다
            var board = new GameObject("SurvivalBoard");
            board.transform.SetParent(ext, false);
            board.transform.localPosition = new Vector3(-2.6f, 1.75f, -1.3f);
            board.AddComponent<SurvivalBoard>();
            Box("Panel", board.transform, Vector3.zero, new Vector3(3.6f, 2f, 0.12f), M("PropDark"));
            Box("PostL", board.transform, new Vector3(-1.6f, -1.25f, 0.05f), new Vector3(0.12f, 1f, 0.12f), M("ShipDark"), false);
            Box("PostR", board.transform, new Vector3(1.6f, -1.25f, 0.05f), new Vector3(0.12f, 1f, 0.12f), M("ShipDark"), false);
            AddLight(board.transform, new Vector3(0f, 0.6f, -1.6f), new Color(0.8f, 0.9f, 1f), 6f, 1.6f).name = "BoardLight";

            // 바위: 건물·길·착륙지를 피해서. 수는 바닥 넓이에 맞춘다
            int rockTarget = Mathf.Clamp(Mathf.RoundToInt((gxMax - gxMin) * (gzMax - gzMin) / 900f), 45, 160);
            int rocks = 0;
            for (int tries = 0; tries < rockTarget * 8 && rocks < rockTarget; tries++)
            {
                var p = new Vector3(Mathf.Lerp(gxMin + 8f, gxMax - 8f, (float)rng.NextDouble()), 0f,
                                    Mathf.Lerp(gzMin + 8f, gzMax - 8f, (float)rng.NextDouble()));
                var p2 = new Vector2(p.x, p.z);
                if (b.Contains(p2)) continue;
                if (Mathf.Abs(p.x) < 12f && p.z < 2f && p.z > padCenter - 17f) continue;
                float s = 1f + (float)rng.NextDouble() * 4f;
                var rock = Box("Rock", ext, p + Vector3.up * (s * 0.3f), new Vector3(s, s * (0.6f + (float)rng.NextDouble() * 0.8f), s * (0.7f + (float)rng.NextDouble() * 0.6f)), M("Rock"));
                rock.transform.localRotation = Quaternion.Euler(rng.Next(-20, 20), rng.Next(0, 360), rng.Next(-20, 20));
                rocks++;
            }
        }

        Rect BuildingBounds()
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in occ.Keys)
            {
                minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y);
                maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y);
            }
            float pad = 6f;
            return Rect.MinMaxRect(minX * cellSize - pad, minY * cellSize - pad, (maxX + 1) * cellSize + pad, (maxY + 1) * cellSize + pad);
        }

        void ApplyAtmosphere()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.06f, 0.065f, 0.08f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.045f);
            RenderSettings.fogDensity = 0.022f;
        }

        // ───────────────────────────────── helpers

        Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        GameObject Marker(string name, SpawnKind kind, Vector3 pos, float yaw, int value)
        {
            var go = new GameObject(name);
            go.transform.SetParent(markerRoot, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var m = go.AddComponent<SpawnMarker>();
            m.kind = kind;
            m.value = value;
            return go;
        }

        GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m, bool collider = true, bool dynamic = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = !dynamic;
            return go;
        }

        GameObject Cyl(string name, Transform parent, Vector3 pos, float diameter, float height, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.isStatic = true;
            return go;
        }

        Material M(string key)
        {
            if (mats.TryGetValue(key, out var cached)) return cached;
            Color c; Color em = Color.black;
            switch (key)
            {
                case "Floor": c = new Color(0.32f, 0.31f, 0.29f); break;
                case "CorridorFloor": c = new Color(0.24f, 0.25f, 0.26f); break;
                case "Ceiling": c = new Color(0.18f, 0.18f, 0.19f); break;
                case "Wall": c = new Color(0.45f, 0.47f, 0.48f); break;
                case "DoorFrame": c = new Color(0.85f, 0.55f, 0.15f); break;
                case "ExitFrame": c = new Color(0.2f, 0.7f, 0.3f); break;
                case "ExitSign": c = new Color(0.1f, 0.6f, 0.2f); em = new Color(0.2f, 1.6f, 0.4f); break;
                case "Prop": c = new Color(0.42f, 0.36f, 0.3f); break;
                case "PropDark": c = new Color(0.2f, 0.2f, 0.22f); break;
                case "Shelf": c = new Color(0.35f, 0.38f, 0.42f); break;
                case "Crate": c = new Color(0.55f, 0.42f, 0.25f); break;
                case "Machine": c = new Color(0.3f, 0.36f, 0.33f); break;
                case "Pipe": c = new Color(0.5f, 0.3f, 0.2f); break;
                case "Pillar": c = new Color(0.4f, 0.4f, 0.4f); break;
                case "Loot": c = new Color(1f, 0.8f, 0.2f); em = new Color(0.6f, 0.45f, 0.05f); break;
                case "Extraction": c = new Color(0.1f, 0.8f, 0.3f); em = new Color(0.1f, 1.2f, 0.35f); break;
                case "Vent": c = new Color(0.55f, 0.08f, 0.06f); em = new Color(0.4f, 0.02f, 0.01f); break;
                case "FixtureOn": c = Color.white; em = new Color(2f, 1.8f, 1.5f); break;
                case "FixtureOff": c = new Color(0.15f, 0.15f, 0.15f); break;
                case "Ground": c = new Color(0.2f, 0.22f, 0.17f); break;
                case "Path": c = new Color(0.33f, 0.3f, 0.26f); break;
                case "Pad": c = new Color(0.3f, 0.3f, 0.32f); break;
                case "Ship": c = new Color(0.62f, 0.64f, 0.66f); break;
                case "ShipDark": c = new Color(0.25f, 0.26f, 0.28f); break;
                case "Glass": c = new Color(0.2f, 0.4f, 0.5f); em = new Color(0.05f, 0.2f, 0.3f); break;
                case "Rock": c = new Color(0.28f, 0.27f, 0.26f); break;
                case "Cabinet": c = new Color(0.23f, 0.27f, 0.34f); break;
                case "CabinetDoor": c = new Color(0.33f, 0.4f, 0.5f); break;
                default: c = Color.magenta; break;
            }
            var mat = LoadOrCreateMaterial("Proto_" + key, c, em);
            mats[key] = mat;
            return mat;
        }

        static Material NewMaterial(string name, Color c, Color em)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", 0.15f);
            if (em != Color.black)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", em);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        static Material LoadOrCreateMaterial(string name, Color c, Color em)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                const string dir = "Assets/Prototype/Materials";
                if (!AssetDatabase.IsValidFolder("Assets/Prototype")) AssetDatabase.CreateFolder("Assets", "Prototype");
                if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Prototype", "Materials");
                string path = $"{dir}/{name}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) return existing;
                var created = NewMaterial(name, c, em);
                AssetDatabase.CreateAsset(created, path);
                return created;
            }
#endif
            return NewMaterial(name, c, em);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (rooms == null) return;
            foreach (var r in rooms)
            {
                var wr = RoomWorldRect(r);
                Gizmos.color = r.kind == RoomKind.Corridor ? new Color(0.5f, 0.7f, 1f, 0.6f) : new Color(1f, 0.6f, 0.2f, 0.8f);
                var center = transform.TransformPoint(new Vector3(wr.center.x, r.height + 0.5f, wr.center.y));
                Gizmos.DrawWireCube(center, new Vector3(wr.width - 0.4f, 0.1f, wr.height - 0.4f));
                if (r.kind != RoomKind.Corridor)
                    Handles.Label(center, $"#{r.id} {r.kind}\n깊이 {r.depth}");
            }
        }
#endif
    }
}
