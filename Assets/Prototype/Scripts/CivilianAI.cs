using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    public enum CivilianState { Wander, Flee, ToExit, FollowMarker, FollowPlayer, Carried, InSafeZone, Dead, ToHide, Hiding }

    /// <summary>
    /// 민간인. 도형으로 표시하고 상태를 색과 문구로 알린다.
    /// 행동 우선순위 (위가 먼저):
    ///  1. 플레이어가 들고 있음 / 따라오게 함 — 적을 봐도 계속 따른다
    ///  2. 적을 봄 — 도망친다
    ///  3. 탈출구가 보임 — 달려가 자력 탈출한다
    ///  4. 안전지대용 유도표시  5. 탈출용 유도표시 — 따라간다
    ///  6. 그 밖 — 숨을 곳에 숨어 지내다가 가끔 나와 주변을 돌아다닌다 (평소 숨기. 가장 낮은 순위)
    /// 평소 숨기는 다른 일을 끊지 않는다. 따라오기·들림·도망·탈출구·유도표시를 마치고 돌아다니기 시작하면
    /// "돌아다니는 시간"을 새로 센 뒤에야 숨으러 간다.
    /// 안전지대에 들어가면 스스로 나오지 않는다.
    /// 숨기: 적이 보고 있지 않을 때만 숨을 수 있고, 숨으면 안전하다. 적이 그 방에서 나가야 스스로 나온다.
    /// </summary>
    public class CivilianAI : MonoBehaviour
    {
        public static readonly List<CivilianAI> All = new List<CivilianAI>();

        const float EyeHeight = 1.5f;
        static int serial;

        static readonly Color[] StateColors =
        {
            new Color(0.85f, 0.85f, 0.85f), // Wander
            new Color(1f, 0.5f, 0.1f),      // Flee
            new Color(0.2f, 1f, 0.4f),      // ToExit
            new Color(0.3f, 0.85f, 1f),     // FollowMarker
            new Color(1f, 0.95f, 0.3f),     // FollowPlayer
            new Color(1f, 0.5f, 0.9f),      // Carried
            new Color(0.3f, 0.5f, 1f),      // InSafeZone
            new Color(0.22f, 0.22f, 0.22f), // Dead
            new Color(0.75f, 0.6f, 1f),     // ToHide
            new Color(0.55f, 0.4f, 0.9f),   // Hiding
        };

        public CivilianState State { get; private set; } = CivilianState.Wander;
        public bool IsAlive => State != CivilianState.Dead;
        /// <summary>플레이어가 들고 있는지 (살아 있든 시체든).</summary>
        public bool IsCarried { get; private set; }
        /// <summary>적이 쫓을 수 있는 대상인지. 들려 있거나 안전지대 안에 있으면 쫓지 않는다.</summary>
        public bool IsTargetable =>
            IsAlive && !IsCarried && State != CivilianState.InSafeZone && State != CivilianState.Hiding &&
            SafeZone.FindContaining(transform.position) == null;
        /// <summary>숨을 곳 안에 있는지.</summary>
        public bool IsHidden => State == CivilianState.Hiding;
        /// <summary>플레이어에게 보이는지. 숨어 있으면 숨을 곳이 열려 있을 때만 보인다.</summary>
        public bool IsShown => State != CivilianState.Hiding || (hideSpot != null && hideSpot.IsOpen);

        public Color StateColor => StateColors[(int)State];

        public string StateLabel
        {
            get
            {
                switch (State)
                {
                    case CivilianState.Wander: return "걷는 중";
                    case CivilianState.Flee: return "도망 (뛰는 중)";
                    case CivilianState.ToExit: return "탈출구로 (뛰는 중)";
                    case CivilianState.FollowMarker:
                        return currentMarker != null && currentMarker.kind == GuideMarkerKind.SafeZone
                            ? "유도표시 따라가는 중 (안전지대)" : "유도표시 따라가는 중 (탈출)";
                    case CivilianState.FollowPlayer: return "따라오는 중";
                    case CivilianState.Carried: return "들려 있음";
                    case CivilianState.InSafeZone: return "안전지대";
                    case CivilianState.ToHide: return hideRun ? "숨으러 가는 중 (뛰는 중)" : "숨으러 가는 중";
                    case CivilianState.Hiding: return "숨어 있음";
                    default: return IsCarried ? "시체 (운반 중)" : "시체";
                }
            }
        }

        NavMeshAgent agent;
        CapsuleCollider col;
        Transform body;
        Renderer[] renderers;
        MaterialPropertyBlock block;
        NavMeshPath pathBuffer;
        readonly Vector3[] cornerBuffer = new Vector3[8];

        PrototypePlayer owner;          // 따라가거나 들려 있는 플레이어
        EnemyAI seenEnemy;
        bool seesExit;
        Vector3 lastEnemyPos;
        float fleeUntil, nextPerceive, nextRepath, waitUntil;
        Vector3 moveTarget;
        float moveSetAt;
        readonly List<Vector3> threats = new List<Vector3>();      // 피해야 할 적들의 위치 (마지막으로 본 곳)
        readonly List<Vector3> seenThreats = new List<Vector3>();  // 이번에 보인 적들
        SafeZone zone;
        bool everInSafeZone;

        HidingSpot hideSpot;            // 가고 있거나 들어가 있는 숨을 곳
        bool hideRun;                   // 적을 보고 뛰어서 숨으러 가는 중
        float nextHideAt, hideUntil;    // 다음에 숨으러 갈 시각, 숨어 있을 최소 시각
        float lastScreamAt = -99f;

        GuideMarker currentMarker;
        bool extraWalk;                 // 다음 유도표시가 없어 화살표 방향으로 더 걷는 중
        readonly List<GuideMarker> visited = new List<GuideMarker>();
        readonly List<float> visitedAt = new List<float>();

        PrototypeSettings S => PrototypeSettings.Active;
        PrototypeSettings.CivilianConfig C => PrototypeSettings.Active.civilian;

        // ───────────────────────────────── 생성

        /// <summary>NavMesh 위의 지점에 민간인을 만든다.</summary>
        public static CivilianAI Spawn(Vector3 navPosition)
        {
            var go = new GameObject($"Civilian_{serial++:000}");
            go.transform.SetPositionAndRotation(navPosition, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            var mat = ProtoVisuals.Lit(Color.white, true);
            var body = ProtoVisuals.Primitive(PrimitiveType.Capsule, "Body", go.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 0.9f, 0.6f), mat);
            // 어느 쪽을 보고 있는지 알 수 있게 얼굴 쪽에 작은 상자를 단다
            ProtoVisuals.Primitive(PrimitiveType.Cube, "Face", body.transform, new Vector3(0f, 0.6f, 0.45f), new Vector3(0.25f, 0.12f, 0.35f), mat);

            // 플레이어가 조준할 수 있게 충돌체를 두되, 길을 막지 않도록 트리거로 한다
            var col = go.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            col.radius = 0.3f;
            col.height = 1.8f;
            col.center = new Vector3(0f, 0.9f, 0f);

            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 1.8f;
            // 모퉁이를 돌 때 속도를 덜 잃도록 적(420, 14)보다 빠르게 돌고 가속한다
            agent.angularSpeed = 900f;
            agent.acceleration = 40f;
            agent.stoppingDistance = 0.2f;

            var ai = go.AddComponent<CivilianAI>();
            ai.body = body.transform;
            return ai;
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            col = GetComponent<CapsuleCollider>();
            block = new MaterialPropertyBlock();
            pathBuffer = new NavMeshPath();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            renderers = GetComponentsInChildren<Renderer>();
            nextPerceive = Time.time + Random.Range(0f, 0.2f); // 여러 명이 같은 프레임에 몰리지 않게
            var game = PrototypeGame.Instance;
            if (game != null) game.ReportSpawned();
            nextHideAt = Time.time + Random.Range(0f, S.hiding.wanderDuration.y);
            Enter(CivilianState.Wander);
        }

        // ───────────────────────────────── 매 프레임

        void Update()
        {
            var game = PrototypeGame.Instance;
            if (game != null && game.IsInExit(transform.position) && HandleExit(game)) return;
            if (State == CivilianState.Dead || State == CivilianState.Carried) return;
            if (State == CivilianState.Hiding) { TickHiding(); return; }

            if (Time.time >= nextPerceive)
            {
                nextPerceive = Time.time + 0.2f;
                Perceive(game);
                Decide(game);
            }
            if (!agent.enabled || !agent.isOnNavMesh) return;

            switch (State)
            {
                case CivilianState.Wander:
                    if (ReachedTarget(0.4f))
                    {
                        if (waitUntil == 0f) waitUntil = Time.time + Random.Range(C.wanderWait.x, C.wanderWait.y);
                        else if (Time.time >= waitUntil) { PickWanderPoint(); waitUntil = 0f; }
                    }
                    break;

                case CivilianState.Flee:
                    if (Time.time >= nextRepath)
                    {
                        nextRepath = Time.time + 0.3f;
                        if (NeedNewFleePoint()) PickFleePoint();
                    }
                    break;

                case CivilianState.FollowMarker:
                    TickFollowMarker();
                    break;

                case CivilianState.ToHide:
                    if (hideSpot == null || !hideSpot.HasRoomFor(this) || hideSpot.IsOpen) { Enter(CivilianState.Wander); break; }
                    if (ReachedTarget(0.8f))
                    {
                        // 적이 보고 있으면 숨을 수 없다
                        if (IsWatchedByEnemy()) Enter(seenEnemy != null ? CivilianState.Flee : CivilianState.Wander);
                        else EnterHiding();
                    }
                    break;

                case CivilianState.FollowPlayer:
                    if (owner == null) { Enter(CivilianState.Wander); break; }
                    if (Time.time >= nextRepath)
                    {
                        nextRepath = Time.time + 0.2f;
                        agent.SetDestination(owner.LeaderOf(this).position);
                    }
                    break;
            }
        }

        /// <summary>탈출구 반경 안에 들어왔을 때. 처리했으면(사라졌으면) true.</summary>
        bool HandleExit(PrototypeGame game)
        {
            if (State == CivilianState.InSafeZone || State == CivilianState.Hiding) return false;
            if (State == CivilianState.Dead)
            {
                if (!IsCarried) return false; // 쓰러져 있는 시체는 들고 와야 수거로 친다
                Leave();
                game.ReportCorpseRetrieved(this);
                return true;
            }
            bool byPlayer = State == CivilianState.FollowPlayer || State == CivilianState.Carried;
            Leave();
            if (byPlayer) game.ReportRescued(this);
            else game.ReportSelfEscaped(this);
            return true;
        }

        /// <summary>던전에서 사라진다. 인원 계산에 바로 반영되도록 목록에서 먼저 뺀다.</summary>
        void Leave()
        {
            if (owner != null) owner.Forget(this);
            owner = null;
            if (hideSpot != null) hideSpot.Leave(this);
            hideSpot = null;
            All.Remove(this);
            Destroy(gameObject);
        }

        // ───────────────────────────────── 보기

        void Perceive(PrototypeGame game)
        {
            seenEnemy = null;
            seesExit = false;
            var eye = transform.position + Vector3.up * EyeHeight;
            float halfAngle = C.sightAngle * 0.5f;
            // 도망치는 중에는 뒤를 돌아보며 뛴다고 보고, 시야각을 따지지 않는다
            bool lookAround = State == CivilianState.Flee;

            float best = float.MaxValue;
            seenThreats.Clear();
            var enemies = EnemyAI.All;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                var to = e.transform.position + Vector3.up * 1.2f - eye;
                float d = to.magnitude;
                if (d >= C.sightRange) continue;
                if (!lookAround && d > C.noticeRadius && Vector3.Angle(transform.forward, to) > halfAngle) continue;
                if (Physics.Raycast(eye, to / d, out var hit, d, ~0, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<EnemyAI>() != e) continue;
                seenThreats.Add(e.transform.position);
                if (d < best) { seenEnemy = e; best = d; }
            }
            // 적이 여럿 보이면 모두 피해야 한다. 아무도 안 보일 때는 마지막으로 본 위치들을 그대로 둔다
            if (seenThreats.Count > 0)
            {
                threats.Clear();
                threats.AddRange(seenThreats);
            }

            if (game != null && game.HasExit)
            {
                var to = game.ExitPosition + Vector3.up - eye;
                float d = to.magnitude;
                if (d <= C.sightRange && (d < 2f || Vector3.Angle(transform.forward, to) <= halfAngle) &&
                    (d < 0.5f || !Physics.Raycast(eye, to / d, d - 0.3f, ~0, QueryTriggerInteraction.Ignore)))
                    seesExit = true;
            }
        }

        // ───────────────────────────────── 판단

        void Decide(PrototypeGame game)
        {
            // 플레이어의 지시가 가장 먼저다. 안전지대 안에서는 스스로 움직이지 않는다.
            if (State == CivilianState.FollowPlayer || State == CivilianState.InSafeZone) return;

            var inZone = SafeZone.FindContaining(transform.position);
            if (inZone != null)
            {
                zone = inZone;
                Enter(CivilianState.InSafeZone);
                if (game != null) game.ReportEnteredSafeZone(this, !everInSafeZone);
                everInSafeZone = true;
                return;
            }

            if (seenEnemy != null)
            {
                lastEnemyPos = seenEnemy.transform.position;
                fleeUntil = Time.time + C.fleeMemory;
                if (State != CivilianState.Flee && !(State == CivilianState.ToHide && hideRun)) Scream();
                // 적이 나를 보고 있지 않을 때만 숨을 수 있다. 보고 있으면 도망친다
                if (!IsWatchedByEnemy())
                {
                    if (State == CivilianState.ToHide) { SetHideRun(); return; }
                    var spot = FindHidingSpot();
                    if (spot != null) { GoHide(spot, true); return; }
                }
                if (State != CivilianState.Flee) Enter(CivilianState.Flee);
                return;
            }
            if (State == CivilianState.Flee)
            {
                if (Time.time < fleeUntil)
                {
                    // 적을 따돌렸으면(나를 보는 적이 없으면) 가까운 숨을 곳으로 간다
                    if (!IsWatchedByEnemy())
                    {
                        var spot = FindHidingSpot();
                        if (spot != null) GoHide(spot, true);
                    }
                    return;
                }
                Enter(CivilianState.Wander);
            }

            if (seesExit)
            {
                if (State != CivilianState.ToExit) Enter(CivilianState.ToExit);
                return;
            }
            if (State == CivilianState.ToExit) return; // 한 번 본 탈출구로는 끝까지 간다

            var eye = transform.position + Vector3.up * EyeHeight;
            if (State == CivilianState.FollowMarker)
            {
                if (extraWalk)
                {
                    var found = FindVisibleMarker(eye, true, null);
                    if (found != null) StartMarker(found);
                }
                else if (currentMarker != null && currentMarker.kind == GuideMarkerKind.Exit)
                {
                    // 탈출용을 따라가다가 안전지대용이 보이면 갈아탄다
                    var found = FindVisibleMarker(eye, true, null);
                    if (found != null && found != currentMarker && found.kind == GuideMarkerKind.SafeZone) StartMarker(found);
                }
                return;
            }

            // 돌아다니거나 숨으러 가는 중: 보이는 유도표시가 있으면 따라간다
            var marker = FindVisibleMarker(eye, true, null);
            if (marker != null) { StartMarker(marker); return; }
            if (State == CivilianState.ToHide) return;

            // 평소: 돌아다닐 만큼 돌아다녔으면 숨을 곳을 찾아간다
            if (Time.time >= nextHideAt)
            {
                var spot = FindHidingSpot();
                if (spot != null) GoHide(spot, false);
                else nextHideAt = Time.time + 5f; // 빈 곳이 없으면 조금 뒤에 다시 찾는다
            }
        }

        void Enter(CivilianState s)
        {
            var prev = State;
            State = s;
            waitUntil = 0f;
            nextRepath = 0f;
            // 평소 숨기는 가장 낮은 순위다. 다른 일을 마치고 돌아다니기 시작하면 곧바로 숨으러 가지 않도록
            // 돌아다니는 시간을 새로 센다. (평소 숨기를 하러 가다 자리가 없어 돌아선 경우는 바로 다른 곳을 찾는다.)
            if (s == CivilianState.Wander && prev != CivilianState.Wander && !(prev == CivilianState.ToHide && !hideRun))
            {
                var h = S.hiding;
                nextHideAt = Time.time + Random.Range(h.wanderDuration.x, h.wanderDuration.y);
            }
            if (s != CivilianState.FollowMarker) { currentMarker = null; extraWalk = false; }
            if (s != CivilianState.ToHide && s != CivilianState.Hiding && hideSpot != null)
            {
                hideSpot.Unreserve(this); // 숨으러 가다가 다른 일을 하게 됐다
                hideSpot = null;
            }
            ApplyColor();
            if (!agent.enabled || !agent.isOnNavMesh) return;

            agent.isStopped = false;
            agent.stoppingDistance = 0.2f;
            agent.autoBraking = s != CivilianState.Flee; // 도망칠 때는 목적지 앞에서 속도를 줄이지 않는다
            switch (s)
            {
                case CivilianState.ToHide:
                    agent.speed = hideRun ? C.runSpeed : C.walkSpeed;
                    MoveTo(hideSpot.EntryPoint);
                    break;
                case CivilianState.Wander:
                    agent.speed = C.walkSpeed;
                    PickWanderPoint();
                    break;
                case CivilianState.Flee:
                    agent.speed = C.runSpeed;
                    PickFleePoint();
                    nextRepath = Time.time + 0.3f;
                    break;
                case CivilianState.ToExit:
                    agent.speed = C.runSpeed;
                    agent.SetDestination(PrototypeGame.Instance.ExitPosition);
                    break;
                case CivilianState.FollowMarker:
                    agent.speed = C.walkSpeed;
                    break;
                case CivilianState.FollowPlayer:
                    agent.speed = C.followSpeed;
                    agent.stoppingDistance = C.followStopDistance;
                    break;
                case CivilianState.InSafeZone:
                    agent.speed = C.walkSpeed;
                    if (zone != null) agent.SetDestination(zone.RandomPointInside());
                    else agent.ResetPath();
                    break;
            }
        }

        /// <summary>p 로 걸어간다. 도착 판정(ReachedTarget)에 쓰도록 목적지와 시각을 기억한다.</summary>
        void MoveTo(Vector3 p)
        {
            moveTarget = p;
            moveSetAt = Time.time;
            agent.SetDestination(p);
        }

        /// <summary>
        /// MoveTo 로 정한 곳에 도착했는지. 실제 거리로 판단한다.
        /// 길이 막혀 더 갈 수 없을 때도 멈춰 서지 않도록, 경로가 끝났으면 도착으로 친다.
        /// (목적지를 정한 직후에는 남은 거리가 0 으로 읽히는 일이 있어 잠시 뒤부터 본다.)
        /// </summary>
        bool ReachedTarget(float tolerance)
        {
            var flat = moveTarget - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude <= tolerance * tolerance) return true;
            return Time.time - moveSetAt > 0.5f && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;
        }

        // ───────────────────────────────── 돌아다니기 · 도망

        void PickWanderPoint()
        {
            var o = Random.insideUnitCircle * C.wanderRadius;
            var candidate = transform.position + new Vector3(o.x, 0f, o.y);
            if (NavMesh.SamplePosition(candidate, out var hit, 2.5f, NavMesh.AllAreas))
                MoveTo(hit.position);
        }

        /// <summary>
        /// 도망칠 곳을 다시 골라야 하는지. 방향을 자주 바꾸면 그만큼 느려지므로,
        /// 목적지에 거의 다 왔거나 가는 쪽에 적이 있을 때만 다시 고른다.
        /// </summary>
        bool NeedNewFleePoint()
        {
            if (agent.pathPending) return false;
            if (!agent.hasPath || agent.remainingDistance < 4f) return true;

            var v = agent.desiredVelocity;
            v.y = 0f;
            if (v.sqrMagnitude < 0.01f) return false;
            v.Normalize();
            for (int t = 0; t < threats.Count; t++)
            {
                var toEnemy = threats[t] - transform.position;
                toEnemy.y = 0f;
                float dist = toEnemy.magnitude;
                if (dist > 0.1f && dist < 9f && Vector3.Dot(v, toEnemy / dist) > 0.4f) return true;
            }
            return false;
        }

        /// <summary>
        /// 적에게서 멀어지는 지점을 고른다. 보이는 적이 여럿이면 모두 피한다.
        /// 적 쪽으로 출발하는 길, 적 옆을 지나는 길, 너무 돌아가는 길은 고르지 않는다.
        /// </summary>
        void PickFleePoint()
        {
            var pos = transform.position;
            if (threats.Count == 0) threats.Add(lastEnemyPos);

            // 가까운 적일수록 크게 쳐서, 적들에게서 멀어지는 방향을 구한다
            var away = Vector3.zero;
            for (int t = 0; t < threats.Count; t++)
            {
                var d = pos - threats[t];
                d.y = 0f;
                float m = d.sqrMagnitude;
                if (m > 0.01f) away += d / m;
            }
            if (away.sqrMagnitude < 1e-6f) away = transform.forward;
            away.Normalize();

            float bestScore = float.MinValue;
            Vector3 bestPos = pos;
            bool found = false;
            for (int i = 0; i < 12; i++)
            {
                // 첫 후보는 정반대 방향, 나머지는 좌우로 넓게 벌린다 (양쪽에서 몰릴 때 옆으로 빠질 수 있게)
                float angle = i == 0 ? 0f : Random.Range(-150f, 150f);
                var dir = Quaternion.Euler(0f, angle, 0f) * away;
                float reach = C.fleeDistance * (i == 0 ? 1f : Random.Range(0.7f, 1.3f));
                if (!NavMesh.SamplePosition(pos + dir * reach, out var hit, 3f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(pos, hit.position, NavMesh.AllAreas, pathBuffer) ||
                    pathBuffer.status != NavMeshPathStatus.PathComplete) continue;
                int n = pathBuffer.GetCornersNonAlloc(cornerBuffer);
                if (n < 2) continue;

                var first = cornerBuffer[1] - cornerBuffer[0];
                first.y = 0f;
                bool hasFirst = first.sqrMagnitude > 0.01f;
                if (hasFirst) first.Normalize();

                float length = 0f;
                for (int k = 1; k < n; k++) length += Vector3.Distance(cornerBuffer[k - 1], cornerBuffer[k]);

                bool bad = false;
                float nearestThreat = float.MaxValue;
                for (int t = 0; t < threats.Count && !bad; t++)
                {
                    var toThreat = threats[t] - pos;
                    toThreat.y = 0f;
                    float threatDist = toThreat.magnitude;
                    // 가까운 적 쪽으로 출발하는 길
                    if (hasFirst && threatDist > 0.1f && threatDist < 12f && Vector3.Dot(first, toThreat / threatDist) > 0.3f) bad = true;
                    // 적 옆을 지나는 길
                    for (int k = 1; k < n && !bad; k++)
                        if ((cornerBuffer[k] - threats[t]).sqrMagnitude < 9f) bad = true;
                    nearestThreat = Mathf.Min(nearestThreat, Vector3.Distance(hit.position, threats[t]));
                }
                if (bad) continue;

                // 적에게서 멀수록 좋고, 곧게 갈 수 있는 길일수록 좋다 (꺾을 때마다 느려진다)
                float straight = Vector3.Distance(pos, hit.position);
                float score = nearestThreat - (length - straight) * 0.7f - (n - 2) * 0.8f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = hit.position;
                    found = true;
                }
            }
            if (found) agent.SetDestination(bestPos);
            else PickWanderPoint(); // 빠져나갈 길이 없다: 일단 움직인다
        }

        // ───────────────────────────────── 유도표시

        void StartMarker(GuideMarker m)
        {
            currentMarker = m;
            extraWalk = false;
            if (State != CivilianState.FollowMarker) Enter(CivilianState.FollowMarker);
            else ApplyColor();
            if (agent.enabled && agent.isOnNavMesh) MoveTo(m.NavPoint);
        }

        void TickFollowMarker()
        {
            if (currentMarker == null) { Enter(CivilianState.Wander); return; }
            var G = S.guideMarker;

            if (extraWalk)
            {
                if (ReachedTarget(0.5f)) Enter(CivilianState.Wander);
                return;
            }

            if (!ReachedTarget(G.arriveDistance)) return;

            // 유도표시에 도착: 화살표 방향 쪽의 가까운 다음 유도표시를 찾는다
            MarkVisited(currentMarker);
            var next = FindVisibleMarker(transform.position + Vector3.up * EyeHeight, false, currentMarker);
            if (next != null) { StartMarker(next); return; }

            // 다음 것이 보이지 않으면 화살표 방향으로 더 걷는다
            extraWalk = true;
            var start = currentMarker.NavPoint;
            var end = start + currentMarker.Direction * G.extraWalkDistance;
            if (NavMesh.Raycast(start, end, out var edge, NavMesh.AllAreas)) end = edge.position;
            MoveTo(end);
        }

        /// <summary>
        /// 보이는 유도표시 가운데 하나를 고른다. 안전지대용이 먼저, (from 이 있으면) 그 화살표 방향 쪽이 먼저, 그다음 가까운 순.
        /// useViewAngle 이 false 면 주위를 둘러본다고 보고 시야각을 따지지 않는다.
        /// </summary>
        GuideMarker FindVisibleMarker(Vector3 eye, bool useViewAngle, GuideMarker from)
        {
            var G = S.guideMarker;
            var list = GuideMarker.All;
            GuideMarker best = null;
            float bestScore = float.MaxValue;
            float halfAngle = C.sightAngle * 0.5f;

            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == from || IsVisited(m)) continue;
                var to = m.SightPoint - eye;
                float d = to.magnitude;
                if (d > G.recognitionDistance) continue;
                if (useViewAngle && d > 2f && Vector3.Angle(transform.forward, to) > halfAngle) continue;
                if (d > 0.5f && Physics.Raycast(eye, to / d, d - 0.2f, ~0, QueryTriggerInteraction.Ignore)) continue;

                float score = d;
                if (m.kind != GuideMarkerKind.SafeZone) score += 1000f;
                if (from != null)
                {
                    var flat = m.NavPoint - from.NavPoint;
                    flat.y = 0f;
                    if (Vector3.Angle(from.Direction, flat) > G.arrowConeAngle * 0.5f) score += 100f;
                }
                if (score < bestScore) { best = m; bestScore = score; }
            }
            return best;
        }

        void MarkVisited(GuideMarker m)
        {
            float memory = S.guideMarker.memorySeconds;
            for (int i = visited.Count - 1; i >= 0; i--)
                if (visited[i] == null || Time.time - visitedAt[i] > memory) { visited.RemoveAt(i); visitedAt.RemoveAt(i); }
            visited.Add(m);
            visitedAt.Add(Time.time);
        }

        bool IsVisited(GuideMarker m)
        {
            float memory = S.guideMarker.memorySeconds;
            for (int i = 0; i < visited.Count; i++)
                if (visited[i] == m && Time.time - visitedAt[i] <= memory) return true;
            return false;
        }

        // ───────────────────────────────── 플레이어가 부르는 것

        /// <summary>플레이어를 따라오기 시작한다.</summary>
        public void StartFollowing(PrototypePlayer player)
        {
            if (!IsAlive || IsCarried) return;
            owner = player;
            if (State == CivilianState.Hiding) LeaveHiding(CivilianState.FollowPlayer);
            else Enter(CivilianState.FollowPlayer);
        }

        /// <summary>따라오기를 풀고 자율 행동으로 돌아간다.</summary>
        public void StopFollowing()
        {
            if (State != CivilianState.FollowPlayer) return;
            owner = null;
            Enter(CivilianState.Wander);
        }

        /// <summary>플레이어가 든다. 살아 있는 민간인과 시체 모두 들 수 있다.</summary>
        public void BeginCarry(PrototypePlayer player, Transform anchor)
        {
            owner = player;
            IsCarried = true;
            if (State == CivilianState.Hiding)
            {
                // 숨을 곳에서 바로 들고 나온다
                if (hideSpot != null) hideSpot.Leave(this);
                hideSpot = null;
                SetShown(true);
            }
            if (IsAlive)
            {
                if (agent.enabled) { if (agent.isOnNavMesh) agent.ResetPath(); agent.enabled = false; }
                State = CivilianState.Carried;
                currentMarker = null;
                extraWalk = false;
            }
            col.enabled = false;
            transform.SetParent(anchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            body.localPosition = Vector3.zero;
            body.localRotation = Quaternion.Euler(0f, 0f, 90f);
            ApplyColor();
        }

        /// <summary>
        /// 내려놓는다. 살아 있는 민간인은 걸을 수 있는 곳에만 내려놓을 수 있다 (forced 면 가장 가까운 곳으로 옮긴다).
        /// 내려놓지 못했으면 false.
        /// </summary>
        public bool EndCarry(Vector3 dropPos, bool forced)
        {
            bool onMesh = NavMesh.SamplePosition(dropPos, out var hit, forced ? 40f : 1.5f, NavMesh.AllAreas);
            if (IsAlive && !onMesh) return false;

            transform.SetParent(null, true);
            transform.SetPositionAndRotation(onMesh ? hit.position : dropPos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            IsCarried = false;
            owner = null;
            col.enabled = true;
            if (IsAlive)
            {
                PoseStanding();
                agent.enabled = true;
                Enter(CivilianState.Wander);
            }
            else
            {
                PoseLying();
                ApplyColor();
            }
            return true;
        }

        /// <summary>적에게 잡혀 죽는다. 시체는 쓰러진 채로 남는다.</summary>
        public void Kill()
        {
            if (!IsAlive || IsCarried) return;
            if (owner != null) owner.Forget(this);
            owner = null;
            if (hideSpot != null) hideSpot.Leave(this);
            hideSpot = null;
            if (agent.enabled) { if (agent.isOnNavMesh) agent.ResetPath(); agent.enabled = false; }
            State = CivilianState.Dead;
            currentMarker = null;
            PoseLying();
            ApplyColor();
            ProtoAudio.PlayAt(ProtoSound.CivilianDeath, transform.position + Vector3.up, 0.5f, S.audio.civilianHearDistance);
            var game = PrototypeGame.Instance;
            if (game != null) game.ReportDied(this);
        }

        /// <summary>적을 보고 놀라 지르는 소리. 같은 민간인이 연달아 지르지 않게 간격을 둔다.</summary>
        void Scream()
        {
            if (Time.time - lastScreamAt < 6f) return;
            lastScreamAt = Time.time;
            ProtoAudio.PlayAt(ProtoSound.CivilianScream, transform.position + Vector3.up * 1.5f, 0.7f,
                S.audio.civilianHearDistance, Random.Range(0.9f, 1.15f));
        }

        // ───────────────────────────────── 숨기

        /// <summary>지금 나를 보고 있는 적이 있는지.</summary>
        bool IsWatchedByEnemy()
        {
            var p = transform.position + Vector3.up * 1.2f;
            var enemies = EnemyAI.All;
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i].CanSeePoint(p)) return true;
            return false;
        }

        /// <summary>찾는 거리 안에서 자리가 있고 걸어서 갈 수 있는 가장 가까운 숨을 곳. 바로 옆에 적이 있는 곳은 고르지 않는다.</summary>
        HidingSpot FindHidingSpot()
        {
            float radius = S.hiding.searchRadius;
            float bestDist = radius * radius;
            HidingSpot best = null;
            var list = HidingSpot.All;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.IsOpen || !s.HasRoomFor(this)) continue;
                float d = (s.transform.position - transform.position).sqrMagnitude;
                if (d >= bestDist || s.EnemyNear(4f)) continue;
                best = s;
                bestDist = d;
            }
            if (best == null) return null;
            if (!NavMesh.CalculatePath(transform.position, best.EntryPoint, NavMesh.AllAreas, pathBuffer) ||
                pathBuffer.status != NavMeshPathStatus.PathComplete) return null;
            return best;
        }

        /// <summary>spot 으로 숨으러 간다. run 이면 뛰어서 간다 (적을 봤을 때).</summary>
        void GoHide(HidingSpot spot, bool run)
        {
            if (hideSpot != null && hideSpot != spot) hideSpot.Unreserve(this);
            hideSpot = spot;
            hideRun = run;
            spot.Reserve(this);
            Enter(CivilianState.ToHide);
        }

        void SetHideRun()
        {
            if (hideRun) return;
            hideRun = true;
            if (agent.enabled) agent.speed = C.runSpeed;
        }

        void EnterHiding()
        {
            var spot = hideSpot;
            spot.Enter(this);
            if (agent.enabled) { if (agent.isOnNavMesh) agent.ResetPath(); agent.enabled = false; }
            col.enabled = false;
            transform.SetPositionAndRotation(spot.InsidePoint, spot.transform.rotation);
            State = CivilianState.Hiding;
            currentMarker = null;
            extraWalk = false;
            var h = S.hiding;
            hideUntil = Time.time + Random.Range(h.hideDuration.x, h.hideDuration.y);
            ApplyColor();
            SetShown(spot.IsOpen);
        }

        /// <summary>숨어 있는 동안: 숨어 있을 만큼 있었고 그 방에 적이 없으면 스스로 나온다. 플레이어가 열어 둔 동안에는 나오지 않는다.</summary>
        void TickHiding()
        {
            if (Time.time < nextPerceive) return;
            nextPerceive = Time.time + 0.5f;
            if (hideSpot == null) { LeaveHiding(CivilianState.Wander); return; }
            if (Time.time >= hideUntil && !hideSpot.IsOpen && !hideSpot.EnemyInRoom()) LeaveHiding(CivilianState.Wander);
        }

        /// <summary>숨을 곳에서 나와 next 상태가 된다.</summary>
        void LeaveHiding(CivilianState next)
        {
            var spot = hideSpot;
            var outPos = spot != null ? spot.EntryPoint : transform.position;
            if (spot != null) spot.Leave(this);
            hideSpot = null;
            if (NavMesh.SamplePosition(outPos, out var hit, 2f, NavMesh.AllAreas)) outPos = hit.position;
            transform.position = outPos;
            SetShown(true);
            col.enabled = true;
            agent.enabled = true;
            var h = S.hiding;
            nextHideAt = Time.time + Random.Range(h.wanderDuration.x, h.wanderDuration.y);
            Enter(next);
        }

        /// <summary>모습을 보이거나 감춘다. 숨을 곳이 열리고 닫힐 때 숨을 곳이 부른다.</summary>
        public void SetShown(bool shown)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = shown;
        }

        // ───────────────────────────────── 표시

        void PoseStanding()
        {
            body.localPosition = new Vector3(0f, 0.9f, 0f);
            body.localRotation = Quaternion.identity;
            col.direction = 1;
            col.center = new Vector3(0f, 0.9f, 0f);
        }

        void PoseLying()
        {
            body.localPosition = new Vector3(0f, 0.3f, 0f);
            body.localRotation = Quaternion.Euler(90f, 0f, 0f);
            col.direction = 2;
            col.center = new Vector3(0f, 0.3f, 0f);
        }

        void ApplyColor()
        {
            if (renderers == null) return;
            var c = StateColor;
            block.SetColor(ProtoVisuals.BaseColorId, c);
            block.SetColor(ProtoVisuals.EmissionColorId, c * 0.6f);
            for (int i = 0; i < renderers.Length; i++) renderers[i].SetPropertyBlock(block);
        }
    }
}
