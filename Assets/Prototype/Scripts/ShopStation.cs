using UnityEngine;

namespace DungeonProto
{
    /// <summary>
    /// 착륙지의 상점. 플레이어가 구매 가능 거리 안에 있으면 숫자 키 7·8·9·0 으로 물품을 산다.
    /// 가격과 거리는 PrototypeSettings 의 "상점 · 시작 지급품"에서 바꾼다. 구매 처리는 PrototypeGame.TryBuy.
    /// </summary>
    public class ShopStation : MonoBehaviour
    {
        public static ShopStation Instance { get; private set; }

        Transform sign;
        Renderer signRenderer;
        Camera cam;
        float nextVisibilityCheck;

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        void Start()
        {
            sign = ProtoVisuals.WorldText("Sign", transform, new Vector3(0f, 2.9f, 0f), 0.07f, new Color(1f, 0.85f, 0.3f)).transform;
            sign.GetComponent<TextMesh>().text = "상점";
            signRenderer = sign.GetComponent<Renderer>();
        }

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null || sign == null) return;

            // 글자는 벽 너머로도 비쳐 보이는 방식으로 그려지므로, 가려지지 않았을 때만 켠다
            if (Time.time >= nextVisibilityCheck)
            {
                nextVisibilityCheck = Time.time + 0.2f;
                bool visible = (cam.transform.position - sign.position).sqrMagnitude < 60f * 60f &&
                               !Physics.Linecast(cam.transform.position, sign.position, ~0, QueryTriggerInteraction.Ignore);
                if (signRenderer.enabled != visible) signRenderer.enabled = visible;
            }

            // 간판은 어느 쪽에서 봐도 읽히게 카메라를 향한다
            var to = sign.position - cam.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) sign.rotation = Quaternion.LookRotation(to);
        }
    }
}
