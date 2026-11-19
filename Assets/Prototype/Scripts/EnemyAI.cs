using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    public enum EnemyState { Patrol, Investigate, Chase, Search, Catch, Cooldown }

    /// <summary>
    /// 시설 내부를 배회하다 플레이어를 보거나 들으면 추적하는 적.
    /// Patrol → (소리) Investigate / (시야) Chase → (놓침) Search → Patrol, 접촉 시 Catch.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyAI : MonoBehaviour
    {
        [Header("Refs")]
        public Animator animator;

        [Header("Speeds (m/s)")]
        public float patrolSpeed = 1.6f;
        public float investigateSpeed = 2.4f;
        public float chaseSpeed = 4.2f;

        [Header("Perception")]
        public float sightRange = 16f;
        [Range(10f, 180f)] public float sightAngle = 120f;
        public float eyeHeight = 1.6f;
        public float hearSprintRange = 11f;
        public float hearWalkRange = 4.5f;
        public float loseSightTime = 4f;
        public float catchDistance = 1.3f;

        [Header("Debug")]
        public bool showStateLabel = true;

        public EnemyState State { get; private set; } = EnemyState.Patrol;

        NavMeshAgent agent;
        DungeonGenerator dungeon;
        PrototypePlayer player;
        Vector3 lastKnown;
        float stateTimer, lastSeenTime, nextPerceive, waitUntil;
        bool canSee;
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int CatchHash = Animator.StringToHash("Catch");

        public void Init(DungeonGenerator d) => dungeon = d;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController == null)
                Debug.LogWarning($"[EnemyAI] {name}: Animator 에 컨트롤러가 없습니다. Tools > Dungeon Prototype > Build Civilian Enemy 를 다시 실행하세요.", this);
        }

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
                    if (canSee) agent.SetDestination(player.transform.position);
                    else if (Time.time - lastSeenTime > loseSightTime) Enter(EnemyState.Search);
                    else agent.SetDestination(lastKnown);

                    if (canSee && Vector3.Distance(transform.position, player.transform.position) < catchDistance)
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
                    FaceTowards(player.transform.position);
                    if (stateTimer > 0.6f && player != null && stateTimer < 10f)
                    {
                        player.Caught(this);
                        stateTimer = 10f; // 한 번만
                    }
                    if (stateTimer > 11.5f) Enter(EnemyState.Cooldown);
                    break;

                case EnemyState.Cooldown:
                    if (stateTimer > 4f) Enter(EnemyState.Patrol);
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
            switch (s)
            {
                case EnemyState.Patrol:
                case EnemyState.Cooldown:
                    agent.speed = patrolSpeed; agent.stoppingDistance = 0.3f;
                    PickPatrolPoint();
                    break;
                case EnemyState.Investigate:
                    agent.speed = investigateSpeed; agent.stoppingDistance = 0.5f;
                    agent.SetDestination(lastKnown);
                    break;
                case EnemyState.Chase:
                    agent.speed = chaseSpeed; agent.stoppingDistance = 0.8f;
                    break;
                case EnemyState.Search:
                    agent.speed = investigateSpeed; agent.stoppingDistance = 0.5f;
                    agent.SetDestination(lastKnown);
                    break;
                case EnemyState.Catch:
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                    if (animator != null) animator.SetTrigger(CatchHash);
                    break;
            }
        }

        void Perceive()
        {
            canSee = false;
            if (player == null || State == EnemyState.Catch || State == EnemyState.Cooldown) return;

            var eye = transform.position + Vector3.up * eyeHeight;
            var target = player.transform.position + Vector3.up * (player.IsCrouching ? 0.8f : 1.5f);
            var to = target - eye;
            float dist = to.magnitude;

            float range = player.IsCrouching ? sightRange * 0.7f : sightRange;
            if (dist < range && Vector3.Angle(transform.forward, to) < sightAngle * 0.5f || dist < 1.8f)
            {
                if (Physics.Raycast(eye, to.normalized, out var hit, dist + 0.2f, ~0, QueryTriggerInteraction.Ignore) &&
                    hit.transform.GetComponentInParent<PrototypePlayer>() == player)
                {
                    canSee = true;
                    lastSeenTime = Time.time;
                    lastKnown = player.transform.position;
                    if (State != EnemyState.Chase) Enter(EnemyState.Chase);
                    return;
                }
            }

            // 소리: 달리기는 멀리, 걷기는 가까이, 웅크리면 무음
            if (State == EnemyState.Chase) return;
            float noise = player.NoiseRadius(hearSprintRange, hearWalkRange);
            if (noise > 0f && Vector3.Distance(transform.position, player.transform.position) < noise)
            {
                lastKnown = player.transform.position;
                if (State != EnemyState.Investigate || stateTimer > 1f) Enter(EnemyState.Investigate);
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
            if (NavMesh.SamplePosition(candidate, out var hit, 2.5f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }

        void OnGUI()
        {
            if (!showStateLabel) return;
            var cam = Camera.main;
            if (cam == null) return;
            var sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.1f);
            if (sp.z < 0f || sp.z > 25f) return;
            var prev = GUI.color;
            GUI.color = State == EnemyState.Chase || State == EnemyState.Catch ? Color.red :
                        State == EnemyState.Investigate || State == EnemyState.Search ? Color.yellow : Color.white;
            GUI.Label(new Rect(sp.x - 50f, Screen.height - sp.y, 100f, 20f), State.ToString());
            GUI.color = prev;
        }

        void OnDrawGizmosSelected()
        {
            var eye = transform.position + Vector3.up * eyeHeight;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            var l = Quaternion.Euler(0f, -sightAngle * 0.5f, 0f) * transform.forward;
            var r = Quaternion.Euler(0f, sightAngle * 0.5f, 0f) * transform.forward;
            Gizmos.DrawRay(eye, l * sightRange);
            Gizmos.DrawRay(eye, r * sightRange);
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, hearSprintRange);
        }
    }
}
