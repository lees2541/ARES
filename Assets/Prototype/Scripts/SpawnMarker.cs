using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DungeonProto
{
    public enum SpawnKind { PlayerSpawn, Loot, EnemySpawn, Extraction }

    /// <summary>
    /// 생성기가 배치하는 게임플레이용 마커. 나중에 실제 스폰 로직이 이 마커들을 읽어 쓰면 된다.
    /// </summary>
    public class SpawnMarker : MonoBehaviour
    {
        public SpawnKind kind;
        [Tooltip("Loot 마커일 때 아이템 가치")]
        public int value;

        public static readonly List<SpawnMarker> All = new List<SpawnMarker>();

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static SpawnMarker FindFirst(SpawnKind kind)
        {
            foreach (var m in All)
                if (m != null && m.kind == kind) return m;
            return null;
        }

        static Color KindColor(SpawnKind k)
        {
            switch (k)
            {
                case SpawnKind.PlayerSpawn: return Color.cyan;
                case SpawnKind.Loot: return new Color(1f, 0.85f, 0.2f);
                case SpawnKind.EnemySpawn: return Color.red;
                default: return Color.green;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = KindColor(kind);
            switch (kind)
            {
                case SpawnKind.PlayerSpawn:
                    Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
                    Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 1.5f);
                    break;
                case SpawnKind.Extraction:
                    Gizmos.DrawWireCube(transform.position + Vector3.up * 1.5f, new Vector3(3f, 3f, 3f));
                    break;
                case SpawnKind.EnemySpawn:
                    Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, 0.8f);
                    break;
                default:
                    Gizmos.DrawWireCube(transform.position, Vector3.one * 0.3f);
                    break;
            }
#if UNITY_EDITOR
            if (kind != SpawnKind.Loot)
                Handles.Label(transform.position + Vector3.up * 2.2f, kind.ToString());
#endif
        }
    }
}
