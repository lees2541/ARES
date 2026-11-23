using System.Text;
using UnityEngine;

namespace DungeonProto
{
    /// <summary>
    /// 입구 옆 생존률 표시판. 생존률과 구출·자력 탈출·사망·시체 수거 수를 보여 준다.
    /// 생존률 = (구출 + 자력 탈출) ÷ (구출 + 자력 탈출 + 사망). 계산은 PrototypeGame.TryGetSurvivalRate.
    /// 글자는 표시판의 -Z 쪽(착륙지 쪽)에서 읽힌다.
    /// </summary>
    public class SurvivalBoard : MonoBehaviour
    {
        TextMesh text;
        readonly StringBuilder sb = new StringBuilder(128);
        int lastVersion = -1;
        float nextRefresh, nextVisibilityCheck;
        Renderer textRenderer;
        Camera cam;

        void Start()
        {
            text = ProtoVisuals.WorldText("Text", transform, new Vector3(0f, 0f, -0.09f), 0.045f, Color.white);
            text.text = "생존률";
            textRenderer = text.GetComponent<Renderer>();
        }

        void LateUpdate()
        {
            // 글자는 벽 너머로도 비쳐 보이는 방식으로 그려지므로, 앞에서 가려지지 않고 보일 때만 켠다
            if (Time.time < nextVisibilityCheck) return;
            nextVisibilityCheck = Time.time + 0.2f;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            var target = text.transform.position - transform.forward * 0.05f;
            var to = cam.transform.position - target;
            bool visible = Vector3.Dot(to, -transform.forward) > 0f && to.sqrMagnitude < 60f * 60f &&
                           !Physics.Linecast(cam.transform.position, target, ~0, QueryTriggerInteraction.Ignore);
            if (textRenderer.enabled != visible) textRenderer.enabled = visible;
        }

        void Update()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.5f;
            var game = PrototypeGame.Instance;
            if (game == null || game.Version == lastVersion) return;
            lastVersion = game.Version;

            sb.Length = 0;
            sb.Append("<size=90>생존률 ");
            if (game.TryGetSurvivalRate(out float percent)) sb.Append(Mathf.RoundToInt(percent)).Append('%');
            else sb.Append("--");
            sb.Append("</size>\n");
            sb.Append("구출 ").Append(game.Rescued).Append("   자력 탈출 ").Append(game.SelfEscaped).Append('\n');
            sb.Append("사망 ").Append(game.Dead).Append("   시체 수거 ").Append(game.CorpsesRetrieved);
            text.text = sb.ToString();
        }
    }
}
