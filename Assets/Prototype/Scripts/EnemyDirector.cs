using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto
{
    /// <summary>
    /// 플레이 시작 시 시설 내부 NavMesh 를 굽고, EnemySpawn(벤트) 마커에서 적을 순차 스폰한다.
    /// DungeonGenerator 와 같은 GameObject 에 붙인다.
    /// </summary>
    [RequireComponent(typeof(DungeonGenerator))]
    public class EnemyDirector : MonoBehaviour
    {
        public GameObject enemyPrefab;
        [Tooltip("첫 스폰까지 대기(초)")]
        public float firstSpawnDelay = 2f;
        [Tooltip("다음 벤트 스폰까지 간격(초)")]
        public float spawnInterval = 6f;
        [Tooltip("0 이면 벤트 수만큼")]
        public int maxEnemies = 0;

        public readonly List<EnemyAI> Enemies = new List<EnemyAI>();
        public float LastBakeMs { get; private set; }

        DungeonGenerator dungeon;
        NavMeshSurface surface;

        void Awake()
        {
            dungeon = GetComponent<DungeonGenerator>();
            BakeNavMesh();
        }

        IEnumerator Start()
        {
            if (enemyPrefab == null) { Debug.LogWarning("[EnemyDirector] enemyPrefab 이 비어 있습니다."); yield break; }
            var vents = new List<SpawnMarker>();
            foreach (var m in GetComponentsInChildren<SpawnMarker>())
                if (m.kind == SpawnKind.EnemySpawn) vents.Add(m);
            int count = maxEnemies > 0 ? Mathf.Min(maxEnemies, vents.Count) : vents.Count;

            yield return new WaitForSeconds(firstSpawnDelay);
            for (int i = 0; i < count; i++)
            {
                Spawn(vents[i].transform.position, vents[i].name);
                if (i < count - 1) yield return new WaitForSeconds(spawnInterval);
            }
        }

        EnemyAI Spawn(Vector3 pos, string from)
        {
            if (!NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[EnemyDirector] {from} 근처에 NavMesh 가 없어 스폰 실패");
                return null;
            }
            var go = Instantiate(enemyPrefab, hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.name = $"{enemyPrefab.name}_{Enemies.Count}";
            var ai = go.GetComponent<EnemyAI>();
            ai.Init(dungeon);
            Enemies.Add(ai);
            return ai;
        }

        /// <summary>시설 내부(건물 영역)만 NavMesh 로 굽는다 → 적은 건물 밖으로 나가지 않는다.</summary>
        public void BakeNavMesh()
        {
            if (dungeon == null) dungeon = GetComponent<DungeonGenerator>();
            if (dungeon.rooms == null || dungeon.rooms.Count == 0) return;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var r in dungeon.rooms)
            {
                minX = Mathf.Min(minX, r.rect.xMin); minY = Mathf.Min(minY, r.rect.yMin);
                maxX = Mathf.Max(maxX, r.rect.xMax); maxY = Mathf.Max(maxY, r.rect.yMax);
            }
            float cs = dungeon.cellSize;
            var min = new Vector3(minX * cs - 0.5f, -0.5f, minY * cs - 0.5f);
            var max = new Vector3(maxX * cs + 0.5f, 2.5f, maxY * cs + 0.5f); // 천장(≥3.2m)은 볼륨 밖

            surface = GetComponent<NavMeshSurface>();
            if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.center = (min + max) * 0.5f;
            surface.size = max - min;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            // 움직이는 전리품은 굽기에서 제외
            foreach (var m in GetComponentsInChildren<SpawnMarker>())
                if (m.kind == SpawnKind.Loot && m.GetComponent<NavMeshModifier>() == null)
                    m.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            surface.BuildNavMesh();
            LastBakeMs = (float)sw.Elapsed.TotalMilliseconds;
        }
    }
}
