using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    /// <summary>
    /// 민간인 수를 유지하는 관리자. 안전지대 밖 던전 안에 살아 있는 민간인 수를 최소~최대로 맞춘다.
    ///  - 최소보다 적으면 바로 채운다.
    ///  - 구출·사망·안전지대 도착 때는 최대보다 적을 때만 한 명이 나타난다.
    /// DungeonGenerator 와 같은 GameObject 에 붙인다.
    /// </summary>
    [RequireComponent(typeof(DungeonGenerator))]
    public class CivilianDirector : MonoBehaviour
    {
        /// <summary>안전지대 밖 던전 안에 살아 있는 민간인 수.</summary>
        public int OutsideAlive { get; private set; }
        /// <summary>안전지대 안에 있는 민간인 수.</summary>
        public int InSafeZone { get; private set; }

        DungeonGenerator dungeon;
        PrototypePlayer player;
        float nextCheck;
        bool ready;
        NavMeshPath pathBuffer;

        PrototypeSettings.PopulationConfig P => PrototypeSettings.Active.population;

        void Awake()
        {
            dungeon = GetComponent<DungeonGenerator>();
            pathBuffer = new NavMeshPath();
        }

        IEnumerator Start()
        {
            player = FindAnyObjectByType<PrototypePlayer>();
            yield return null; // NavMesh 는 EnemyDirector.Awake 에서 굽는다. 한 프레임 뒤에 시작한다
            ready = true;
            FillToMinimum();
        }

        void Update()
        {
            if (!ready || Time.time < nextCheck) return;
            nextCheck = Time.time + P.checkInterval;
            FillToMinimum();
        }

        /// <summary>구출·사망·안전지대 도착으로 안전지대 밖 민간인이 한 명 줄었을 때 부른다.</summary>
        public void OnCivilianLeft()
        {
            if (!ready) return;
            Recount();
            if (OutsideAlive < P.maxOutside) SpawnOne();
        }

        void FillToMinimum()
        {
            Recount();
            int guard = 0;
            while (OutsideAlive < P.minOutside && guard++ < 50)
                if (!SpawnOne()) break;
        }

        public void Recount()
        {
            int outside = 0, inZone = 0;
            var all = CivilianAI.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c.IsAlive) continue;
                if (c.State == CivilianState.InSafeZone) inZone++;
                else outside++;
            }
            OutsideAlive = outside;
            InSafeZone = inZone;
        }

        bool SpawnOne()
        {
            if (!TryPickSpawnPoint(out var pos)) return false;
            CivilianAI.Spawn(pos);
            OutsideAlive++;
            return true;
        }

        /// <summary>입구가 아닌 구역의 무작위 지점. 플레이어와 떨어져 있고 안전지대 밖이어야 한다.</summary>
        bool TryPickSpawnPoint(out Vector3 pos)
        {
            pos = default;
            var rooms = dungeon.rooms;
            if (rooms == null || rooms.Count == 0) return false;
            float cs = dungeon.cellSize;
            float minDist = P.minSpawnDistanceFromPlayer;

            for (int tries = 0; tries < 60; tries++)
            {
                var r = rooms[Random.Range(0, rooms.Count)];
                if (r.kind == RoomKind.Entrance) continue;
                var local = new Vector3(
                    (r.rect.x + Random.Range(0.2f, r.rect.width - 0.2f)) * cs, 0f,
                    (r.rect.y + Random.Range(0.2f, r.rect.height - 0.2f)) * cs);
                var candidate = dungeon.transform.TransformPoint(local);
                if (!NavMesh.SamplePosition(candidate, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                if (SafeZone.FindContaining(hit.position) != null) continue;
                // 기계와 벽 사이처럼 고립된 틈에는 내보내지 않는다. 탈출구까지 걸어갈 수 있는 곳이어야 한다
                var game = PrototypeGame.Instance;
                if (game != null && game.HasExit &&
                    (!NavMesh.CalculatePath(hit.position, game.ExitPosition, NavMesh.AllAreas, pathBuffer) ||
                     pathBuffer.status != NavMeshPathStatus.PathComplete)) continue;
                // 자리를 못 찾는 일이 없도록, 시도가 길어지면 거리 조건을 절반으로 낮춘다
                float need = tries < 40 ? minDist : minDist * 0.5f;
                if (player != null && (hit.position - player.transform.position).sqrMagnitude < need * need) continue;
                pos = hit.position;
                return true;
            }
            return false;
        }
    }
}
