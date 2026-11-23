using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    public enum EnemyState { Patrol, Investigate, Chase, Search, Catch, Cooldown, Stunned }

    /// <summary>
    /// 시설 내부를 배회하다 플레이어나 민간인을 보거나 소리를 들으면 추적하는 적.
    /// Patrol → (소리) Investigate / (시야) Chase → (놓침) Search → Patrol, 접촉 시 Catch.
    /// 총에 맞으면 Stunned(무력화) → 끝나면 마지막으로 본 곳을 Search.
    /// 속도·시야 같은 수치는 PrototypeSettings 의 "적"에서 바꾼다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyAI : MonoBehaviour
    {
        public static readonly List<EnemyAI> All = new List<EnemyAI>();

        static readonly string[] Labels = { "순찰", "조사", "추격", "수색", "잡는 중", "대기", "무력화" };

        [Header("Refs")]
        public Animator animator;
        [Tooltip("눈 높이(m). 모델에 맞춘 값이라 프리팹에 둔다.")]
        public float eyeHeight = 1.6f;

        public EnemyState State { get; private set; } = EnemyState.Patrol;
        public string StateLabel => Labels[(int)State];

        NavMeshAgent agent;
        DungeonGenerator dungeon;
        PrototypePlayer player;

        // 지금 쫓는 대상 (플레이어이거나 민간인)
        Transform target;
        PrototypePlayer targetPlayer;
        CivilianAI targetCivilian;

        Vector3 lastKnown;
        bool hasLastKnown;
        float stateTimer, lastSeenTime, nextPerceive, waitUntil, stunUntil;
        bool canSee;
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int CatchHash = Animator.StringToHash("Catch");

        PrototypeSettings.EnemyConfig E => PrototypeSettings.Active.enemy;

        public void Init(DungeonGenerator d) => dungeon = d;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            // 안전지대(별도 NavMesh 영역)는 지나갈 수 없다
            agent.areaMask = NavMesh.AllAreas & ~(1 << SafeZone.NavArea);
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController == null)
                Debug.LogWarning($"[EnemyAI] {name}: Animator 에 컨트롤러가 없습니다. Tools > Dungeon Prototype > Build Civilian Enemy 를 다시 실행하세요.", this);
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            player = FindAnyObjectByType<PrototypePlayer>();
            Enter(EnemyState.Patrol);
        }

        void Update()
        {
            if (!agent.isOnNavMesh) return;
            stateTimer += Time.deltaTime;
            if (Time.time >= nextPerceive) { Perceive(); nextPerceive = Time.time + 0.15f; }

            switch (State)
            {
                case EnemyState.Patrol:
                    if (Arrived() && Time.time >= waitUntil)
                    {
                        if (waitUntil > 0f) { PickPatrolPoint(); waitUntil = 0f; }
                        else waitUntil = Time.time + Random.Range(1f, 3.5f);
                    }
                    break;

                case EnemyState.Investigate:
                    if (Arrived())
                    {
                        if (waitUntil == 0f) waitUntil = Time.time + 2.5f;
                        LookAround();
                        if (Time.time >= waitUntil) Enter(EnemyState.Patrol);
                    }
                    break;

                case EnemyState.Chase:
                    if (target == null) canSee = false; // 쫓던 민간인이 탈출했거나 사라짐
                    if (canSee) agent.SetDestination(target.position);
                    else if (Time.time - lastSeenTime > E.loseSightTime) { Enter(EnemyState.Search); break; }
                    else agent.SetDestination(lastKnown);

                    if (canSee && Vector3.Distance(transform.position, target.position) < E.catchDistance)
                        Enter(EnemyState.Catch);
                    break;

                case EnemyState.Search:
                    if (Arrived())
                    {
                        if (waitUntil == 0f) waitUntil = Time.time + 3f;
                        LookAround();
                        if (Time.time >= waitUntil) Enter(EnemyState.Patrol);
                    }
                    break;

                case EnemyState.Catch:
                    if (target != null) FaceTowards(target.position);
                    if (stateTimer > 0.6f && stateTimer < 10f)
                    {
                        ApplyCatch();
                        stateTimer = 10f; // 한 번만
                    }
                    if (stateTimer > 11.5f) Enter(EnemyState.Cooldown);
                    break;

                case EnemyState.Cooldown:
                    if (stateTimer > 4f) Enter(EnemyState.Patrol);
                    break;

                case EnemyState.Stunned:
                    if (Time.time >= stunUntil)
                    {
                        // 무력화가 끝나면 마지막으로 본 곳을 수색한다
                        if (!hasLastKnown) lastKnown = transform.position;
                        Enter(EnemyState.Search);
                    }
                    break;
            }

            if (animator != null) animator.SetFloat(SpeedHash, agent.velocity.magnitude, 0.1f, Time.deltaTime);
        }

        void Enter(EnemyState s)
        {
            State = s;
            stateTimer = 0f;
            waitUntil = 0f;
            agent.isStopped = false;
            var e = E;
            switch (s)
            {
                case EnemyState.Patrol:
                case EnemyState.Cooldown:
                    agent.speed = e.patrolSpeed; agent.stoppingDistance = 0.3f;
                    PickPatrolPoint();
                    break;
                case EnemyState.Investigate:
                    agent.speed = e.investigateSpeed; agent.stoppingDistance = 0.5f;
                    agent.SetDestination(lastKnown);
                    break;
                case EnemyState.Chase:
                    agent.speed = e.chaseSpeed; agent.stoppingDistance = 0.8f;
                    break;
                case EnemyState.Search:
                    agent.speed = e.investigateSpeed; agent.stoppingDistance = 0.5f;
                    agent.SetDestination(lastKnown);
                    break;
                case EnemyState.Catch:
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                    if (animator != null) animator.SetTrigger(CatchHash);
                    break;
                case EnemyState.Stunned:
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                    break;
            }
        }

        // ───────────────────────────────── 밖에서 부르는 것

        /// <summary>총에 맞음 → seconds 동안 무력화. 잡는 중이었다면 취소된다.</summary>
        public void Stun(float seconds)
        {
            if (!agent.isOnNavMesh) return;
            stunUntil = Time.time + seconds;
            canSee = false;
            if (State != EnemyState.Stunned) Enter(EnemyState.Stunned);
        }

        /// <summary>pos 에서 난 소리를 들음 → 쫓는 중이 아니면 조사하러 간다.</summary>
        public void HearNoise(Vector3 pos)
        {
            if (State == EnemyState.Chase || State == EnemyState.Catch || State == EnemyState.Cooldown || State == EnemyState.Stunned) return;
            lastKnown = pos;
            hasLastKnown = true;
            if (State != EnemyState.Investigate || stateTimer > 1f) Enter(EnemyState.Investigate);
        }

        // ───────────────────────────────── 감지

        void Perceive()
        {
            canSee = false;
            if (State == EnemyState.Catch || State == EnemyState.Cooldown || State == EnemyState.Stunned) return;

            LeaveSafeZone();

            var e = E;
            var game = PrototypeGame.Instance;
            var eye = transform.position + Vector3.up * eyeHeight;

            // 디코이: 범위 안에 있으면 보이지 않아도 든 사람을 1순위로 쫓는다
            if (game != null && player != null && game.DecoyHolder == player.transform && game.DecoyAffects(transform.position))
            {
                See(player.transform, player, null);
                return;
            }

            float playerDist = float.MaxValue;
            bool playerVisible = player != null && CanSeePlayer(eye, out playerDist);
            if (!playerVisible) playerDist = float.MaxValue;
            var civilian = FindVisibleCivilian(eye, out float civilianDist);

            bool pickPlayer;
            switch (e.targetPriority)
            {
                case EnemyTargetPriority.CivilianFirst:
                    pickPlayer = civilian == null;
                    break;
                case EnemyTargetPriority.Nearest:
                    // 대상이 계속 바뀌지 않게, 지금 쫓는 쪽을 2m 만큼 유리하게 본다
                    float bias = targetPlayer != null ? 2f : targetCivilian != null ? -2f : 0f;
                    pickPlayer = playerDist - bias <= civilianDist;
                    break;
                default:
                    pickPlayer = playerVisible;
                    break;
            }

            if (pickPlayer && playerVisible) { See(player.transform, player, null); return; }
            if (civilian != null) { See(civilian.transform, null, civilian); return; }
            if (playerVisible) { See(player.transform, player, null); return; }

            // 소리: 달리기는 멀리, 걷기는 가까이, 웅크리면 무음
            if (State == EnemyState.Chase || player == null) return;
            float noise = player.NoiseRadius(e.hearSprintRange, e.hearWalkRange);
            if (noise > 0f && Vector3.Distance(transform.position, player.transform.position) < noise)
                HearNoise(player.transform.position);
        }

        void See(Transform t, PrototypePlayer p, CivilianAI c)
        {
            target = t;
            targetPlayer = p;
            targetCivilian = c;
            canSee = true;
            lastSeenTime = Time.time;
            lastKnown = t.position;
            hasLastKnown = true;
            if (State != EnemyState.Chase) Enter(EnemyState.Chase);
        }

        bool CanSeePlayer(Vector3 eye, out float dist)
        {
            dist = 0f;
            // 안전지대 안의 대상은 쫓지 않는다 (프로토타입 임시 가정)
            if (SafeZone.FindContaining(player.transform.position) != null) return false;

            var e = E;
            var targetPos = player.transform.position + Vector3.up * (player.IsCrouching ? 0.8f : 1.5f);
            var to = targetPos - eye;
            dist = to.magnitude;
            float range = player.IsCrouching ? e.sightRange * 0.7f : e.sightRange;
            bool inView = dist < range && Vector3.Angle(transform.forward, to) < e.sightAngle * 0.5f || dist < 1.8f;
            if (!inView) return false;
            return Physics.Raycast(eye, to / dist, out var hit, dist + 0.2f, ~0, QueryTriggerInteraction.Ignore) &&
                   hit.transform.GetComponentInParent<PrototypePlayer>() == player;
        }

        CivilianAI FindVisibleCivilian(Vector3 eye, out float bestDist)
        {
            var e = E;
            CivilianAI best = null;
            bestDist = float.MaxValue;
            var list = CivilianAI.All;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (!c.IsTargetable) continue;
                var to = c.transform.position + Vector3.up * 1.2f - eye;
                float d = to.magnitude;
                if (d >= bestDist) continue;
                bool inView = d < e.sightRange && Vector3.Angle(transform.forward, to) < e.sightAngle * 0.5f || d < 1.8f;
                if (!inView) continue;
                // 민간인의 충돌체는 트리거라 광선에 걸리지 않는다. 사이에 무언가 걸리면 가려진 것이다
                if (d > 0.3f && Physics.Raycast(eye, to / d, d - 0.1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                best = c;
                bestDist = d;
            }
            return best;
        }

        void ApplyCatch()
        {
            if (targetCivilian != null)
            {
                if (targetCivilian.IsAlive && !targetCivilian.IsCarried) targetCivilian.Kill();
            }
            else if (targetPlayer != null)
            {
                targetPlayer.TakeHit(E.damagePerCatch, this);
            }
        }

        /// <summary>서 있는 곳이 새로 설치된 안전지대 안이면 밖으로 나온다.</summary>
        void LeaveSafeZone()
        {
            if (SafeZone.All.Count == 0 || !SafeZone.InAnyNavBlock(transform.position)) return;
            if (NavMesh.SamplePosition(transform.position, out var hit, 20f, agent.areaMask))
            {
                agent.Warp(hit.position);
                if (State == EnemyState.Patrol) PickPatrolPoint();
            }
        }

        bool Arrived() => !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;

        void LookAround()
        {
            transform.Rotate(0f, Mathf.Sin(Time.time * 1.3f) * 90f * Time.deltaTime, 0f);
        }

        void FaceTowards(Vector3 p)
        {
            var d = p - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 540f * Time.deltaTime);
        }

        void PickPatrolPoint()
        {
            Vector3 candidate = transform.position + Random.insideUnitSphere * 15f;
            if (dungeon != null && dungeon.rooms != null && dungeon.rooms.Count > 0)
            {
                var r = dungeon.rooms[Random.Range(0, dungeon.rooms.Count)];
                float cs = dungeon.cellSize;
                candidate = dungeon.transform.TransformPoint(new Vector3(
                    (r.rect.x + Random.Range(0.2f, r.rect.width - 0.2f)) * cs, 0f,
                    (r.rect.y + Random.Range(0.2f, r.rect.height - 0.2f)) * cs));
            }
            candidate.y = 0f;
            if (NavMesh.SamplePosition(candidate, out var hit, 2.5f, agent.areaMask))
                agent.SetDestination(hit.position);
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            var e = E;
            var eye = transform.position + Vector3.up * eyeHeight;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            var l = Quaternion.Euler(0f, -e.sightAngle * 0.5f, 0f) * transform.forward;
            var r = Quaternion.Euler(0f, e.sightAngle * 0.5f, 0f) * transform.forward;
            Gizmos.DrawRay(eye, l * e.sightRange);
            Gizmos.DrawRay(eye, r * e.sightRange);
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, e.hearSprintRange);
        }
    }
}
