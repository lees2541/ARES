using UnityEngine;

namespace DungeonProto
{
    public enum ShopItem { Ammo, GuideMarker, SafeZone, Decoy }

    /// <summary>
    /// 프로토타입의 게임 상태(재화, 갖고 있는 물품, 구조 성과, 디코이)를 한곳에서 바꾸는 관리자.
    /// 다른 코드는 상태를 직접 고치지 않고 여기의 메서드를 부른다.
    /// DungeonGenerator 와 같은 GameObject 에 붙인다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PrototypeGame : MonoBehaviour
    {
        [Tooltip("게임 설정 에셋. Play 전에 이 에셋에서 값을 바꾼다.")]
        public PrototypeSettings settings;

        public static PrototypeGame Instance { get; private set; }

        // 재화 · 물품
        public int Currency { get; private set; }
        public int Ammo { get; private set; }
        public int GuideMarkers { get; private set; }
        public int SafeZones { get; private set; }
        public bool HasDecoy { get; private set; }

        // 구조 성과
        public int Spawned { get; private set; }
        public int Rescued { get; private set; }
        public int SelfEscaped { get; private set; }
        public int Dead { get; private set; }
        public int CorpsesRetrieved { get; private set; }

        /// <summary>표시가 바뀌어야 할 때마다 늘어나는 번호. 화면 표시가 이 값으로 갱신 여부를 판단한다.</summary>
        public int Version { get; private set; }

        // 화면에 잠깐 띄우는 알림
        public string LastMessage { get; private set; }
        /// <summary>알림이 올 때마다 늘어나는 번호. 화면 표시가 새 알림을 알아채는 데 쓴다.</summary>
        public int MessageSerial { get; private set; }
        public float MessageUntil { get; private set; }

        // 디코이
        public Transform DecoyHolder { get; private set; }
        float decoyUntil, decoyReadyAt;

        // 탈출구
        bool exitSearched;
        Vector3 exitPosition;
        bool hasExit;

        CivilianDirector director;
        PrototypeSettings S => PrototypeSettings.Active;

        void Awake()
        {
            Instance = this;
            if (settings != null) PrototypeSettings.Active = settings;
            else Debug.LogWarning("[PrototypeGame] settings 가 비어 있습니다. Tools > Dungeon Prototype > Setup Current Scene 을 실행하세요.", this);

            director = GetComponent<CivilianDirector>();
            var shop = S.shop;
            Currency = shop.startCurrency;
            Ammo = shop.startAmmo;
            GuideMarkers = shop.startGuideMarkers;
            SafeZones = shop.startSafeZones;
            HasDecoy = shop.startWithDecoy;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ───────────────────────────────── 탈출구

        public bool HasExit { get { FindExit(); return hasExit; } }
        public Vector3 ExitPosition { get { FindExit(); return exitPosition; } }

        void FindExit()
        {
            if (exitSearched) return;
            var m = SpawnMarker.FindFirst(SpawnKind.Extraction);
            if (m == null) return; // 마커가 아직 등록되기 전일 수 있으므로 다음에 다시 찾는다
            exitSearched = true;
            hasExit = true;
            exitPosition = m.transform.position;
        }

        /// <summary>탈출구 반경 안인지 (높이는 무시).</summary>
        public bool IsInExit(Vector3 p)
        {
            if (!HasExit) return false;
            float r = S.rescue.exitRadius;
            float dx = p.x - exitPosition.x, dz = p.z - exitPosition.z;
            return dx * dx + dz * dz <= r * r;
        }

        // ───────────────────────────────── 구조 성과

        public void ReportSpawned()
        {
            Spawned++;
            Version++;
        }

        /// <summary>플레이어가 데리고(따라오기·들기) 탈출구에 도착.</summary>
        public void ReportRescued(CivilianAI c)
        {
            Rescued++;
            AddCurrency(S.reward.rescue, "구출");
            if (director != null) director.OnCivilianLeft();
        }

        /// <summary>민간인이 스스로 탈출구에 도착.</summary>
        public void ReportSelfEscaped(CivilianAI c)
        {
            SelfEscaped++;
            AddCurrency(S.reward.selfEscape, "자력 탈출");
            if (director != null) director.OnCivilianLeft();
        }

        public void ReportDied(CivilianAI c)
        {
            Dead++;
            Toast("민간인 사망");
            if (director != null) director.OnCivilianLeft();
        }

        public void ReportCorpseRetrieved(CivilianAI c)
        {
            CorpsesRetrieved++;
            AddCurrency(S.reward.corpse, "시체 수거");
        }

        /// <summary>민간인이 안전지대에 들어감. first 는 그 민간인이 처음 들어간 것인지.</summary>
        public void ReportEnteredSafeZone(CivilianAI c, bool first)
        {
            if (first && S.reward.safeZoneArrival > 0) AddCurrency(S.reward.safeZoneArrival, "안전지대 도착");
            else Version++;
            if (director != null) director.OnCivilianLeft();
        }

        /// <summary>생존률(%) = (구출 + 자력 탈출) ÷ (구출 + 자력 탈출 + 사망). 결과가 난 민간인이 없으면 false.</summary>
        public bool TryGetSurvivalRate(out float percent)
        {
            int survived = Rescued + SelfEscaped;
            int total = survived + Dead;
            percent = total > 0 ? 100f * survived / total : 0f;
            return total > 0;
        }

        // ───────────────────────────────── 재화 · 물품

        void AddCurrency(int amount, string reason)
        {
            if (amount > 0)
            {
                Currency += amount;
                Toast($"{reason}  재화 +{amount}");
            }
            else Toast(reason);
        }

        public bool IsNearShop(Vector3 p)
        {
            var shop = ShopStation.Instance;
            if (shop == null) return false;
            var d = p - shop.transform.position;
            d.y = 0f;
            float r = S.shop.shopRange;
            return d.sqrMagnitude <= r * r;
        }

        public static int PriceOf(ShopItem item)
        {
            var shop = PrototypeSettings.Active.shop;
            switch (item)
            {
                case ShopItem.Ammo: return shop.ammoPackPrice;
                case ShopItem.GuideMarker: return shop.guideMarkerPrice;
                case ShopItem.SafeZone: return shop.safeZonePrice;
                default: return shop.decoyPrice;
            }
        }

        public bool TryBuy(ShopItem item)
        {
            if (item == ShopItem.Decoy && HasDecoy) { Toast("디코이는 이미 갖고 있습니다"); return false; }
            int price = PriceOf(item);
            if (Currency < price) { Toast("재화가 부족합니다"); return false; }
            Currency -= price;
            switch (item)
            {
                case ShopItem.Ammo: Ammo += S.shop.ammoPackSize; Toast($"탄약 +{S.shop.ammoPackSize}"); break;
                case ShopItem.GuideMarker: GuideMarkers++; Toast("유도표시 +1"); break;
                case ShopItem.SafeZone: SafeZones++; Toast("안전지대 +1"); break;
                case ShopItem.Decoy: HasDecoy = true; Toast("디코이를 샀습니다"); break;
            }
            return true;
        }

        public bool TryUseAmmo()
        {
            if (Ammo <= 0) { Toast("탄약이 없습니다"); return false; }
            Ammo--;
            Version++;
            return true;
        }

        public bool TryUseGuideMarker()
        {
            if (GuideMarkers <= 0) { Toast("유도표시가 없습니다"); return false; }
            GuideMarkers--;
            Version++;
            return true;
        }

        public bool TryUseSafeZone()
        {
            if (SafeZones <= 0) { Toast("안전지대가 없습니다"); return false; }
            SafeZones--;
            Version++;
            return true;
        }

        // ───────────────────────────────── 디코이 · 소리

        public bool DecoyActive => DecoyHolder != null && Time.time < decoyUntil;
        public float DecoyTimeLeft => DecoyActive ? decoyUntil - Time.time : 0f;
        public float DecoyCooldownLeft => Mathf.Max(0f, decoyReadyAt - Time.time);

        /// <summary>holder 가 디코이를 사용한다. 지속 시간 동안 범위 안의 적이 holder 를 1순위로 쫓는다.</summary>
        public bool TryActivateDecoy(Transform holder)
        {
            if (!HasDecoy) { Toast("디코이가 없습니다"); return false; }
            if (Time.time < decoyReadyAt) { Toast("디코이를 아직 다시 쓸 수 없습니다"); return false; }
            DecoyHolder = holder;
            decoyUntil = Time.time + S.decoy.duration;
            decoyReadyAt = decoyUntil + S.decoy.cooldown;
            Toast("디코이 작동");
            return true;
        }

        /// <summary>enemyPos 에 있는 적이 지금 디코이에 끌리는지.</summary>
        public bool DecoyAffects(Vector3 enemyPos)
        {
            if (!DecoyActive) return false;
            float range = S.decoy.range;
            return range <= 0f || (enemyPos - DecoyHolder.position).sqrMagnitude <= range * range;
        }

        /// <summary>pos 에서 난 소리를 radius 안의 적에게 알린다. radius 가 0 이하면 아무도 듣지 못한다.</summary>
        public void EmitNoise(Vector3 pos, float radius)
        {
            if (radius <= 0f) return;
            var enemies = EnemyAI.All;
            for (int i = 0; i < enemies.Count; i++)
                if ((enemies[i].transform.position - pos).sqrMagnitude <= radius * radius)
                    enemies[i].HearNoise(pos);
        }

        // ───────────────────────────────── 알림

        public void Toast(string message)
        {
            LastMessage = message;
            MessageSerial++;
            MessageUntil = Time.time + 2.5f;
            Version++;
        }
    }
}
