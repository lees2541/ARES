using System;
using UnityEngine;

namespace DungeonProto
{
    /// <summary>적이 플레이어와 민간인을 함께 봤을 때 누구를 쫓는지.</summary>
    public enum EnemyTargetPriority
    {
        [InspectorName("플레이어 먼저")] PlayerFirst,
        [InspectorName("가까운 쪽")] Nearest,
        [InspectorName("민간인 먼저")] CivilianFirst,
    }

    /// <summary>
    /// 프로토타입의 게임 설정을 한곳에 모은 에셋. Play 를 누르기 전에 여기서 값을 보고 바꾼다.
    /// 기획에서 정하지 않은 수치는 모두 임시 기본값이며, 시험하면서 맞춘다.
    /// (Docs/02_시스템/프로토타입-구조-루프.md)
    /// </summary>
    [CreateAssetMenu(fileName = "PrototypeSettings", menuName = "Dungeon Prototype/Prototype Settings")]
    public class PrototypeSettings : ScriptableObject
    {
        [Setting("맵", "Play 를 누르면 이 값으로 던전을 자동으로 만든다.")]
        public MapConfig map = new MapConfig();

        [Setting("민간인 수 유지", "안전지대 밖 던전 안에 살아 있는 민간인 수를 이 범위로 유지한다.")]
        public PopulationConfig population = new PopulationConfig();

        [Setting("재화 · 보상")]
        public RewardConfig reward = new RewardConfig();

        [Setting("상점 · 시작 지급품")]
        public ShopConfig shop = new ShopConfig();

        [Setting("민간인")]
        public CivilianConfig civilian = new CivilianConfig();

        [Setting("구출")]
        public RescueConfig rescue = new RescueConfig();

        [Setting("유도표시")]
        public GuideMarkerConfig guideMarker = new GuideMarkerConfig();

        [Setting("안전지대")]
        public SafeZoneConfig safeZone = new SafeZoneConfig();

        [Setting("숨을 곳", "민간인이 숨는 캐비닛. 맵에 드문드문 놓이고 플레이어가 사서 설치할 수도 있다.")]
        public HidingConfig hiding = new HidingConfig();

        [Setting("총")]
        public GunConfig gun = new GunConfig();

        [Setting("디코이")]
        public DecoyConfig decoy = new DecoyConfig();

        [Setting("범위 표시기", "유도표시·안전지대·디코이를 쓰기 전에 바닥에 그리는 범위 원.")]
        public RangeIndicatorConfig rangeIndicator = new RangeIndicatorConfig();

        [Setting("플레이어")]
        public PlayerConfig player = new PlayerConfig();

        [Setting("적")]
        public EnemyConfig enemy = new EnemyConfig();

        // ───────────────────────────────── 묶음

        [Serializable]
        public class MapConfig
        {
            [Setting("Play 할 때 자동으로 만들기", "켜면 Play 를 누를 때마다 아래 값으로 던전을 새로 만든다. 끄면 씬에 만들어 둔 맵을 그대로 쓴다.")]
            public bool generateOnPlay = true;
            [Setting("매번 새 시드", "켜면 Play 할 때마다 다른 맵이 나온다. 끄면 아래 시드로 항상 같은 맵이 나온다.")]
            public bool randomSeed = true;
            [Setting("시드", "\"매번 새 시드\"를 껐을 때 쓰는 값. 같은 시드와 같은 설정이면 같은 맵이 나온다.")]
            public int seed = 1234;

            [Setting("목표 구역 수 (복도 포함)", 10, 400, "방과 복도를 합친 수. 실제로 만들어지는 수는 이보다 조금 적다.")]
            public int targetRooms = 150;
            [Setting("맵 가로 (m)", 40f, 600f, "던전이 놓일 수 있는 범위의 가로. 구역 수에 비해 좁으면 빽빽해지고 목표 수를 못 채운다.")]
            public float width = 208f;
            [Setting("맵 세로 (m)", 40f, 600f, "던전이 놓일 수 있는 범위의 세로 (입구에서 안쪽으로).")]
            public float depth = 208f;
            [Setting("순환 통로 확률", 0f, 1f, "이웃한 구역 사이에 통로를 더 뚫을 확률. 높을수록 돌아갈 길이 많아져 복잡해진다.")]
            public float loopChance = 0.3f;
            [Setting("복도 비율", 0f, 1f, "방에서 뻗어 나갈 때 복도를 놓을 확률. 높을수록 복도가 많고 방 사이가 멀어진다.")]
            public float corridorChance = 0.7f;
        }

        [Serializable]
        public class PopulationConfig
        {
            [Setting("최소 인원 (명)", 0, 30, "안전지대 밖에 살아 있는 민간인이 이보다 적으면 바로 채운다. 시작할 때도 이 수만큼 나타난다.")]
            public int minOutside = 7;
            [Setting("최대 인원 (명)", 0, 30, "구출·사망·안전지대 도착으로 새 민간인이 나타날 때, 이 수보다 적을 때만 나타난다.")]
            public int maxOutside = 7;
            [Setting("플레이어와의 최소 거리 (m)", 0, 60, "새 민간인은 플레이어에게서 이만큼 떨어진 곳에만 나타난다.")]
            public float minSpawnDistanceFromPlayer = 15f;
            [Setting("인원 확인 간격 (초)", 0.2f, 5f)]
            public float checkInterval = 1f;
        }

        [Serializable]
        public class RewardConfig
        {
            [Setting("구출 보상", 0, 1000, "플레이어가 데리고(따라오기·들기) 탈출구에 도착한 민간인 1명당.")]
            public int rescue = 100;
            [Setting("자력 탈출 보상", 0, 1000, "민간인이 스스로 탈출구에 도착했을 때. 0 이면 주지 않는다.")]
            public int selfEscape = 50;
            [Setting("시체 수거 보상", 0, 1000, "시체를 들고 탈출구에 도착했을 때. 구출 보상보다 낮게 둔다.")]
            public int corpse = 30;
            [Setting("안전지대 도착 보상", 0, 1000, "민간인이 안전지대에 처음 도착했을 때. 추후 논의 대상이라 기본 0.")]
            public int safeZoneArrival = 0;
        }

        [Serializable]
        public class ShopConfig
        {
            [Setting("구매 가능 거리 (m)", 1, 15, "상점에서 이 거리 안에 있어야 살 수 있다.")]
            public float shopRange = 4f;

            [Setting("탄약 묶음 가격", 0, 1000)]
            public int ammoPackPrice = 20;
            [Setting("탄약 묶음 수량 (발)", 1, 50)]
            public int ammoPackSize = 5;
            [Setting("유도표시 가격 (1개)", 0, 1000, "탈출용·안전지대용은 같은 물품이고, 설치할 때 종류를 고른다.")]
            public int guideMarkerPrice = 15;
            [Setting("안전지대 가격 (1개)", 0, 5000)]
            public int safeZonePrice = 150;
            [Setting("디코이 가격", 0, 5000, "한 번 사면 여러 번 쓸 수 있다.")]
            public int decoyPrice = 100;
            [Setting("숨을 곳 가격 (1개)", 0, 5000)]
            public int hidingSpotPrice = 60;

            [Setting("시작 재화", 0, 5000)]
            public int startCurrency = 100;
            [Setting("시작 탄약 (발)", 0, 100)]
            public int startAmmo = 6;
            [Setting("시작 유도표시 (개)", 0, 100)]
            public int startGuideMarkers = 3;
            [Setting("시작 안전지대 (개)", 0, 20)]
            public int startSafeZones = 0;
            [Setting("디코이를 갖고 시작")]
            public bool startWithDecoy = false;
            [Setting("시작 숨을 곳 (개)", 0, 20)]
            public int startHidingSpots = 0;
        }

        [Serializable]
        public class CivilianConfig
        {
            [Setting("걷는 속도 (m/s)", 0.5f, 6f, "돌아다니거나 유도표시를 따라갈 때.")]
            public float walkSpeed = 1.5f;
            [Setting("뛰는 속도 (m/s)", 1f, 10f, "도망칠 때와 탈출구로 달려갈 때. 적의 추격 속도보다 살짝 빠르면 아슬아슬하게 잡히지 않는다.")]
            public float runSpeed = 4.35f;
            [Setting("따라오는 속도 (m/s)", 1f, 10f, "플레이어를 따라올 때.")]
            public float followSpeed = 4.3f;
            [Setting("따라올 때 간격 (m)", 0.5f, 5f, "앞사람(플레이어 또는 앞의 민간인)과 이만큼 떨어져 멈춘다.")]
            public float followStopDistance = 1.6f;

            [Setting("시야 거리 (m)", 1f, 40f, "적, 탈출구를 알아보는 거리.")]
            public float sightRange = 12f;
            [Setting("시야각 (도)", 10f, 360f)]
            public float sightAngle = 160f;
            [Setting("가까운 적을 알아채는 거리 (m)", 0f, 20f, "이 거리 안의 적은 등 뒤에 있어도 알아챈다 (사이가 가로막혀 있지 않을 때). 0 이면 눈에 보일 때만 알아챈다.")]
            public float noticeRadius = 5f;

            [Setting("돌아다니는 반경 (m)", 1f, 30f)]
            public float wanderRadius = 8f;
            [Setting("돌아다니다 멈춰 서는 시간 (초, 최소~최대)")]
            public Vector2 wanderWait = new Vector2(1f, 3.5f);

            [Setting("한 번에 도망치는 거리 (m)", 2f, 40f, "도망칠 곳을 이 거리쯤에서 고른다. 다 가기 전에 다음 곳을 다시 고른다.")]
            public float fleeDistance = 14f;
            [Setting("적이 안 보인 뒤에도 도망치는 시간 (초)", 0f, 15f)]
            public float fleeMemory = 3f;

            [Setting("상태 문구 표시")]
            public bool showStateLabel = true;
            [Setting("상태 문구가 보이는 거리 (m)", 1f, 80f)]
            public float labelDistance = 25f;
        }

        [Serializable]
        public class RescueConfig
        {
            [Setting("들고 있을 때 이동 속도 배율", 0.1f, 1f, "민간인이나 시체를 들고 있을 때 플레이어 속도에 곱한다.")]
            public float carrySpeedMultiplier = 0.6f;
            [Setting("탈출구 반경 (m)", 0.5f, 10f, "이 안에 들어오면 탈출(구출·자력 탈출·시체 수거)로 처리한다.")]
            public float exitRadius = 2.5f;
        }

        [Serializable]
        public class GuideMarkerConfig
        {
            [Setting("민간인이 알아보는 거리 (m)", 1f, 40f, "설치 전 범위 표시기가 보여 주는 범위.")]
            public float recognitionDistance = 10f;
            [Setting("다음 것이 없을 때 더 걷는 거리 (m)", 0f, 30f, "다음 유도표시가 보이지 않으면 화살표 방향으로 이만큼 더 걷는다.")]
            public float extraWalkDistance = 6f;
            [Setting("화살표 방향으로 치는 각도 (도)", 10f, 360f, "화살표 방향에서 이 각도 안에 있는 유도표시를 먼저 고른다.")]
            public float arrowConeAngle = 120f;
            [Setting("지나온 것을 기억하는 시간 (초)", 0f, 300f, "한 번 지나온 유도표시는 이 시간 동안 다시 따라가지 않는다.")]
            public float memorySeconds = 30f;
            [Setting("도착으로 치는 거리 (m)", 0.3f, 3f)]
            public float arriveDistance = 1f;
            [Setting("설치할 수 있는 거리 (m)", 1f, 12f, "바라보는 방향으로 이 거리 안의 바닥에 붙인다. 벽이나 물건을 보고 있으면 그 앞 바닥에 붙는다.")]
            public float placeDistance = 6f;
        }

        [Serializable]
        public class SafeZoneConfig
        {
            [Setting("반경 (m)", 1f, 12f, "설치 전 범위 표시기가 보여 주는 범위. 복도 폭(4m)보다 크면 복도를 막는다.")]
            public float radius = 3f;
        }

        [Serializable]
        public class HidingConfig
        {
            [Setting("숨어 있는 시간 (초, 최소~최대)", "이 시간이 지나고 그 방에 적이 없으면 스스로 나와 돌아다닌다. 길수록 덜 죽는다.")]
            public Vector2 hideDuration = new Vector2(30f, 60f);
            [Setting("돌아다니는 시간 (초, 최소~최대)", "나와서 이만큼 돌아다닌 뒤 다시 숨을 곳을 찾아간다. 짧을수록 덜 죽는다.")]
            public Vector2 wanderDuration = new Vector2(10f, 25f);
            [Setting("숨을 곳을 찾는 거리 (m)", 3f, 100f, "이 거리 안의 빈 숨을 곳 가운데 가장 가까운 곳으로 간다.")]
            public float searchRadius = 30f;
            [Setting("한 곳에 숨을 수 있는 인원 (명)", 1, 5)]
            public int capacity = 1;
            [Setting("방에 숨을 곳이 놓일 확률", 0f, 1f, "맵을 만들 때 방마다 숨을 곳 하나가 놓일 확률.")]
            public float mapChance = 0.35f;
            [Setting("설치 거리 (m)", 0.8f, 4f, "플레이어 앞 이 거리의 바닥에 설치한다.")]
            public float placeDistance = 1.6f;
            [Setting("자동으로 닫히는 거리 (m)", 2f, 20f, "열어 둔 숨을 곳은 플레이어가 이만큼 멀어지면 닫힌다.")]
            public float autoCloseDistance = 5f;
        }

        [Serializable]
        public class GunConfig
        {
            [Setting("무력화 시간 (초)", 0.5f, 15f, "기획 값 3초.")]
            public float stunSeconds = 3f;
            [Setting("사거리 (m)", 2f, 60f)]
            public float range = 25f;
            [Setting("발사 간격 (초)", 0.05f, 3f)]
            public float fireInterval = 0.5f;
            [Setting("총소리 반경 (m)", 0f, 60f, "이 거리 안의 적이 총소리가 난 곳을 조사하러 온다. 0 이면 부르지 않는다.")]
            public float noiseRadius = 0f;
        }

        [Serializable]
        public class DecoyConfig
        {
            [Setting("지속 시간 (초)", 0.5f, 60f, "사용한 뒤 적이 든 사람을 1순위로 쫓는 시간.")]
            public float duration = 5f;
            [Setting("영향 범위 (m)", 0f, 100f, "이 거리 안의 적이 끌려온다. 0 이면 던전 전체.")]
            public float range = 20f;
            [Setting("재사용 대기 (초)", 0f, 120f, "효과가 끝난 뒤 다시 쓸 수 있을 때까지.")]
            public float cooldown = 8f;
        }

        [Serializable]
        public class RangeIndicatorConfig
        {
            [Setting("범위 원 표시")]
            public bool show = true;
            [Setting("선 굵기 (m)", 0.01f, 0.5f)]
            public float lineWidth = 0.07f;
            [Setting("원을 이루는 선분 수", 12, 128)]
            public int segments = 64;
            [Setting("바닥에서 띄우는 높이 (m)", 0f, 0.5f)]
            public float heightOffset = 0.06f;
            [Setting("설치할 수 있을 때 색")]
            public Color validColor = new Color(0.3f, 1f, 0.5f, 1f);
            [Setting("설치할 수 없을 때 색")]
            public Color invalidColor = new Color(1f, 0.3f, 0.25f, 1f);
        }

        [Serializable]
        public class PlayerConfig
        {
            [Setting("최대 체력", 1, 500)]
            public int maxHealth = 100;
            [Setting("걷는 속도 (m/s)", 1f, 10f)]
            public float walkSpeed = 4f;
            [Setting("달리는 속도 (m/s)", 1f, 14f)]
            public float sprintSpeed = 6.5f;
            [Setting("웅크린 속도 (m/s)", 0.5f, 6f)]
            public float crouchSpeed = 2f;
            [Setting("점프 높이 (m)", 0f, 3f)]
            public float jumpHeight = 1.1f;
            [Setting("스태미너 (달릴 수 있는 초)", 0.5f, 30f)]
            public float staminaMax = 5f;
            [Setting("스태미너 회복 (초당)", 0.05f, 5f)]
            public float staminaRecover = 0.6f;
            [Setting("마우스 감도", 0.01f, 1f)]
            public float mouseSensitivity = 0.1f;
            [Setting("상호작용 거리 (m)", 1f, 8f, "민간인과 숨을 곳에 상호작용할 수 있는 거리.")]
            public float interactDistance = 3.5f;
            [Setting("조준 여유 각도 (도)", 2f, 45f, "조준선에서 이 각도 안에 있으면 정확히 겨누지 않아도 상호작용 대상으로 잡힌다. 클수록 잡기 쉽다.")]
            public float interactAngle = 14f;
        }

        [Serializable]
        public class EnemyConfig
        {
            [Setting("적 수", 0, 30, "0 이면 적이 나오지 않는다. 맵을 자동으로 만들 때는 적이 나오는 벤트도 이 수만큼 놓는다.")]
            public int count = 4;
            [Setting("첫 등장까지 (초)", 0f, 60f)]
            public float firstSpawnDelay = 2f;
            [Setting("다음 등장 간격 (초)", 0f, 60f)]
            public float spawnInterval = 6f;

            [Setting("순찰 속도 (m/s)", 0.5f, 8f)]
            public float patrolSpeed = 1.6f;
            [Setting("조사·수색 속도 (m/s)", 0.5f, 8f)]
            public float investigateSpeed = 2.4f;
            [Setting("추격 속도 (m/s)", 1f, 12f)]
            public float chaseSpeed = 4.2f;

            [Setting("시야 거리 (m)", 1f, 60f)]
            public float sightRange = 16f;
            [Setting("시야각 (도)", 10f, 360f)]
            public float sightAngle = 120f;
            [Setting("달리는 소리를 듣는 거리 (m)", 0f, 40f)]
            public float hearSprintRange = 11f;
            [Setting("걷는 소리를 듣는 거리 (m)", 0f, 40f)]
            public float hearWalkRange = 4.5f;
            [Setting("놓친 뒤 추격을 포기하는 시간 (초)", 0.5f, 30f)]
            public float loseSightTime = 4f;
            [Setting("잡는 거리 (m)", 0.5f, 4f)]
            public float catchDistance = 1.3f;

            [Setting("잡혔을 때 줄어드는 체력", 0, 500, "플레이어가 한 번 잡힐 때마다. 체력이 0 이 되면 착륙지로 돌아간다.")]
            public int damagePerCatch = 34;
            [Setting("함께 보였을 때 쫓는 대상", "플레이어와 민간인이 함께 보일 때. 프로토타입 기본은 플레이어 (추후 회의에서 논의).")]
            public EnemyTargetPriority targetPriority = EnemyTargetPriority.PlayerFirst;

            [Setting("상태 문구 표시")]
            public bool showStateLabel = true;
        }

        // ───────────────────────────────── 지금 쓰는 설정

        static PrototypeSettings active;

        /// <summary>지금 플레이에 쓰는 설정. PrototypeGame 이 씬에 연결된 에셋을 넣는다.</summary>
        public static PrototypeSettings Active
        {
            get
            {
                if (active != null) return active;
                var game = FindAnyObjectByType<PrototypeGame>();
                if (game != null && game.settings != null) return active = game.settings;
                Debug.LogWarning("[PrototypeSettings] 씬에 설정 에셋이 연결돼 있지 않아 기본값을 씁니다. Tools > Dungeon Prototype > Setup Current Scene 을 실행하세요.");
                active = CreateInstance<PrototypeSettings>();
                active.name = "PrototypeSettings (기본값)";
                active.hideFlags = HideFlags.DontSave;
                return active;
            }
            set => active = value;
        }

        void OnValidate()
        {
            if (population.maxOutside < population.minOutside) population.maxOutside = population.minOutside;
            if (civilian.wanderWait.y < civilian.wanderWait.x) civilian.wanderWait.y = civilian.wanderWait.x;
            if (hiding.hideDuration.y < hiding.hideDuration.x) hiding.hideDuration.y = hiding.hideDuration.x;
            if (hiding.wanderDuration.y < hiding.wanderDuration.x) hiding.wanderDuration.y = hiding.wanderDuration.x;
        }
    }
}
