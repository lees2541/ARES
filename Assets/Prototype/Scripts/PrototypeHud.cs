using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonProto
{
    /// <summary>
    /// 프로토타입 화면 표시 (uGUI Canvas). 플레이를 시작할 때 코드로 만든다.
    ///  - 왼쪽 아래: 체력, 스태미너
    ///  - 가운데 아래: 도구 슬롯 5칸 (1~5, 마우스 휠) 과 지금 할 수 있는 일 안내
    ///  - 오른쪽 위: 재화, 던전 안 생존자 수, 안전지대 안 민간인 수, 따라오는 민간인 수
    ///  - 가운데: 조준점, 상호작용 안내, 알림
    ///  - 오른쪽: 상점 (상점 가까이에 있을 때만)
    ///  - 왼쪽 위: 맵 시드, 조작법 (H 로 켜고 끔)
    ///  - 민간인과 적의 머리 위 상태 문구
    /// 글자는 값이 바뀔 때만 다시 쓴다. 한글이 바로 나오도록 TextMeshPro 대신 기본 Text 를 쓴다.
    /// </summary>
    public class PrototypeHud : MonoBehaviour
    {
        static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.07f, 0.86f);
        static readonly Color PanelSelected = new Color(0.17f, 0.2f, 0.26f, 0.95f);
        static readonly Color RowColor = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color TextMain = new Color(0.95f, 0.96f, 0.98f);
        static readonly Color TextDim = new Color(0.72f, 0.76f, 0.82f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
        static readonly Color Bad = new Color(1f, 0.4f, 0.34f);
        static readonly Color Good = new Color(0.4f, 1f, 0.6f);
        static readonly Color HealthColor = new Color(0.92f, 0.24f, 0.24f);
        static readonly Color StaminaColor = new Color(0.9f, 0.92f, 0.96f);

        static readonly string[] ToolNames = { "총", "유도표시\n탈출용", "유도표시\n안전지대용", "안전지대", "디코이", "숨을 곳" };
        static readonly Color[] ToolColors =
        {
            new Color(1f, 0.86f, 0.4f), new Color(0.2f, 1f, 0.35f), new Color(0.25f, 0.6f, 1f),
            new Color(0.3f, 0.5f, 1f), new Color(0.85f, 0.5f, 1f), new Color(0.6f, 0.78f, 0.95f),
        };

        const string Key = "<color=#FFD24D><b>";
        const string KeyEnd = "</b></color>";
        static readonly string PromptFollow = Key + "E" + KeyEnd + " 따라오게 하기      " + Key + "G" + KeyEnd + " 들기";
        static readonly string PromptUnfollow = Key + "E" + KeyEnd + " 따라오기 풀기      " + Key + "G" + KeyEnd + " 들기";
        static readonly string PromptCorpse = Key + "G" + KeyEnd + " 시체 들기";
        static readonly string PromptDrop = Key + "G" + KeyEnd + " 내려놓기";
        static readonly string PromptOpen = Key + "E" + KeyEnd + " 숨을 곳 열어 보기";
        static readonly string PromptTakeOut = "민간인이 숨어 있습니다      " + Key + "E" + KeyEnd + " 데리고 나오기      " + Key + "G" + KeyEnd + " 들고 나오기";
        static readonly string PromptEmpty = "비어 있습니다      " + Key + "E" + KeyEnd + " 닫기";

        static readonly string[,] HelpRows =
        {
            { "W A S D", "이동" }, { "Shift", "달리기 (스태미너 사용)" }, { "C", "웅크리기 (소리가 나지 않음)" }, { "Space", "점프" },
            { "F", "손전등" }, { "1 ~ 6, 휠", "도구 고르기" }, { "마우스 왼쪽", "고른 도구 쓰기" }, { "E", "따라오게 하기 / 숨을 곳 열기" },
            { "G", "민간인·시체 들기 / 내려놓기" }, { "7 8 9 0 -", "상점에서 사기 (상점 가까이에서)" }, { "Esc", "마우스 커서 풀기" }, { "H", "이 조작법 켜고 끄기" },
        };

        PrototypePlayer player;
        PrototypeGame game;
        CivilianDirector director;
        DungeonGenerator dungeon;
        Camera cam;

        Canvas canvas;
        Font font;
        Sprite rounded, circle, vignette;

        // 체력 · 스태미너
        RectTransform healthFill, staminaFill;
        UnityEngine.UI.Text healthText;
        int lastHealth = -1;

        // 도구 슬롯
        readonly RectTransform[] slotRects = new RectTransform[6];
        readonly UnityEngine.UI.Image[] slotPanels = new UnityEngine.UI.Image[6];
        readonly UnityEngine.UI.Text[] slotNames = new UnityEngine.UI.Text[6];
        readonly UnityEngine.UI.Text[] slotCounts = new UnityEngine.UI.Text[6];
        readonly RectTransform[] slotAccents = new RectTransform[6];
        int lastSlotVersion = -1, lastTool = -1;
        bool lastCarrying;
        float nextSlotTick;

        // 재화 · 민간인
        UnityEngine.UI.Text currencyText, outsideText, inZoneText, followerText, mapText;
        int lastCurrency = -1, lastOutside = -1, lastInZone = -1, lastFollowers = -1, lastSeed = -1, lastRooms = -1;

        // 가운데
        readonly UnityEngine.UI.Image[] crosshair = new UnityEngine.UI.Image[5];
        UnityEngine.UI.Image promptPanel, statusPanel, aimRing;
        UnityEngine.UI.Text promptText, statusText, bigText;
        string lastPrompt, lastStatus;

        // 알림
        struct Toast { public string text; public Color color; public float until; }
        readonly List<Toast> toasts = new List<Toast>();
        readonly UnityEngine.UI.Image[] toastPanels = new UnityEngine.UI.Image[4];
        readonly UnityEngine.UI.Text[] toastTexts = new UnityEngine.UI.Text[4];
        int lastMessageSerial;

        // 상점
        GameObject shopRoot;
        UnityEngine.UI.Text shopCurrency;
        readonly UnityEngine.UI.Text[] shopNames = new UnityEngine.UI.Text[5];
        readonly UnityEngine.UI.Text[] shopSubs = new UnityEngine.UI.Text[5];
        readonly UnityEngine.UI.Text[] shopPrices = new UnityEngine.UI.Text[5];
        int lastShopVersion = -1;

        // 조작법, 피격 표시, 머리 위 문구
        GameObject helpRoot;
        float helpAutoHideAt;
        UnityEngine.UI.Image vignetteImage;
        readonly UnityEngine.UI.Text[] worldLabels = new UnityEngine.UI.Text[40];

        void Start()
        {
            player = FindAnyObjectByType<PrototypePlayer>();
            game = PrototypeGame.Instance;
            director = FindAnyObjectByType<CivilianDirector>();
            dungeon = FindAnyObjectByType<DungeonGenerator>();
            if (player == null || game == null) { enabled = false; return; }

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rounded = MakeRounded(32, 11);
            circle = MakeRounded(32, 16);
            vignette = MakeVignette(64);
            Build();
            lastMessageSerial = game.MessageSerial;
            helpAutoHideAt = Time.time + 15f; // 처음 15초 동안 조작법을 보여 준다
        }

        // ───────────────────────────────── 만들기

        void Build()
        {
            var go = new GameObject("PrototypeHudCanvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = go.transform;

            Vector2 TL = new Vector2(0f, 1f), TR = new Vector2(1f, 1f), BL = Vector2.zero, BC = new Vector2(0.5f, 0f);
            Vector2 TC = new Vector2(0.5f, 1f), CC = new Vector2(0.5f, 0.5f), RC = new Vector2(1f, 0.5f), LC = new Vector2(0f, 0.5f);

            // 피격 표시 (화면 가장자리가 붉어진다). 가장 먼저 만들어 다른 것 뒤에 그린다
            var vrt = Node("DamageVignette", root, CC, CC, Vector2.zero, Vector2.zero);
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            vignetteImage = vrt.gameObject.AddComponent<UnityEngine.UI.Image>();
            vignetteImage.sprite = vignette;
            vignetteImage.color = new Color(0.85f, 0.05f, 0.05f, 0f);
            vignetteImage.raycastTarget = false;

            // 머리 위 상태 문구
            var labelRoot = Node("WorldLabels", root, BL, BL, Vector2.zero, Vector2.zero);
            for (int i = 0; i < worldLabels.Length; i++)
            {
                var t = Label("Label", labelRoot, BL, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(320f, 24f), 16, TextMain, TextAnchor.LowerCenter, true);
                var outline = t.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(1.2f, -1.2f);
                t.gameObject.SetActive(false);
                worldLabels[i] = t;
            }

            // 조준하고 있는 대상 위에 그리는 고리
            aimRing = Box("AimRing", labelRoot, BL, CC, Vector2.zero, new Vector2(58f, 58f), Good);
            aimRing.sprite = MakeRing(64, 5f);
            aimRing.type = UnityEngine.UI.Image.Type.Simple;
            aimRing.gameObject.SetActive(false);

            // 왼쪽 아래: 체력 · 스태미너
            var vitals = Box("Vitals", root, BL, BL, new Vector2(36f, 36f), new Vector2(400f, 98f), PanelColor).transform;
            Label("HealthLabel", vitals, TL, TL, new Vector2(20f, -14f), new Vector2(90f, 26f), 17, TextDim, TextAnchor.MiddleLeft);
            LastLabel.text = "체력";
            var hpBack = Box("HealthBar", vitals, TL, TL, new Vector2(112f, -17f), new Vector2(214f, 20f), new Color(1f, 1f, 1f, 0.13f)).transform;
            healthFill = Fill("Fill", hpBack, HealthColor);
            healthText = Label("HealthValue", vitals, TR, TR, new Vector2(-18f, -11f), new Vector2(60f, 32f), 24, TextMain, TextAnchor.MiddleRight, true);
            Label("StaminaLabel", vitals, TL, TL, new Vector2(20f, -54f), new Vector2(90f, 26f), 17, TextDim, TextAnchor.MiddleLeft);
            LastLabel.text = "스태미너";
            var stBack = Box("StaminaBar", vitals, TL, TL, new Vector2(112f, -61f), new Vector2(214f, 12f), new Color(1f, 1f, 1f, 0.13f)).transform;
            staminaFill = Fill("Fill", stBack, StaminaColor);

            // 가운데 아래: 도구 슬롯
            const float slotW = 132f, slotH = 106f, gap = 10f;
            var hotbar = Node("Hotbar", root, BC, BC, new Vector2(0f, 34f), new Vector2(slotW * 6f + gap * 5f, slotH));
            for (int i = 0; i < ToolNames.Length; i++)
            {
                var panel = Box("Slot" + (i + 1), hotbar, BL, BL, new Vector2(i * (slotW + gap), 0f), new Vector2(slotW, slotH), PanelColor);
                slotPanels[i] = panel;
                slotRects[i] = (RectTransform)panel.transform;
                var badge = Box("Key", panel.transform, TL, TL, new Vector2(8f, -8f), new Vector2(26f, 26f), new Color(1f, 1f, 1f, 0.92f)).transform;
                var keyText = Label("KeyText", badge, CC, CC, Vector2.zero, new Vector2(26f, 26f), 17, new Color(0.08f, 0.09f, 0.12f), TextAnchor.MiddleCenter, true);
                keyText.text = (i + 1).ToString();
                slotNames[i] = Label("Name", panel.transform, TC, TC, new Vector2(10f, -9f), new Vector2(94f, 44f), 15, TextMain, TextAnchor.MiddleCenter);
                slotNames[i].text = ToolNames[i];
                slotCounts[i] = Label("Count", panel.transform, BC, BC, new Vector2(0f, 14f), new Vector2(slotW - 12f, 30f), 22, TextMain, TextAnchor.MiddleCenter, true);
                var accent = Box("Accent", panel.transform, BL, BL, new Vector2(12f, 7f), new Vector2(slotW - 24f, 4f), ToolColors[i], false);
                slotAccents[i] = (RectTransform)accent.transform;
            }

            // 슬롯 위: 지금 할 수 있는 일 / 할 수 없는 이유
            statusPanel = Box("Status", root, BC, BC, new Vector2(0f, 156f), new Vector2(560f, 38f), PanelColor);
            statusText = Label("Text", statusPanel.transform, CC, CC, Vector2.zero, new Vector2(900f, 38f), 18, TextMain, TextAnchor.MiddleCenter);
            statusPanel.gameObject.SetActive(false);

            // 오른쪽 위: 재화 · 민간인
            var info = Box("Info", root, TR, TR, new Vector2(-36f, -36f), new Vector2(320f, 196f), PanelColor).transform;
            Label("CurrencyLabel", info, TL, TL, new Vector2(22f, -18f), new Vector2(100f, 26f), 17, TextDim, TextAnchor.MiddleLeft);
            LastLabel.text = "재화";
            currencyText = Label("Currency", info, TR, TR, new Vector2(-22f, -8f), new Vector2(220f, 48f), 38, Gold, TextAnchor.MiddleRight, true);
            Box("Line", info, TL, TL, new Vector2(22f, -64f), new Vector2(276f, 1f), new Color(1f, 1f, 1f, 0.16f), false);
            outsideText = InfoRow(info, -76f, "던전 안 생존자");
            inZoneText = InfoRow(info, -114f, "안전지대 안");
            followerText = InfoRow(info, -152f, "따라오는 중");

            // 왼쪽 위: 맵 정보, 조작법
            mapText = Label("MapInfo", root, TL, TL, new Vector2(38f, -28f), new Vector2(700f, 22f), 15, TextDim, TextAnchor.MiddleLeft, false, true);
            helpRoot = Box("Help", root, TL, TL, new Vector2(36f, -60f), new Vector2(440f, 54f + HelpRows.GetLength(0) * 27f), PanelColor).gameObject;
            Label("Title", helpRoot.transform, TL, TL, new Vector2(20f, -12f), new Vector2(300f, 28f), 20, TextMain, TextAnchor.MiddleLeft, true);
            LastLabel.text = "조작법";
            for (int i = 0; i < HelpRows.GetLength(0); i++)
            {
                float y = -46f - i * 27f;
                Label("Key", helpRoot.transform, TL, TL, new Vector2(20f, y), new Vector2(130f, 24f), 16, Gold, TextAnchor.MiddleLeft, true);
                LastLabel.text = HelpRows[i, 0];
                Label("Desc", helpRoot.transform, TL, TL, new Vector2(150f, y), new Vector2(280f, 24f), 16, TextMain, TextAnchor.MiddleLeft);
                LastLabel.text = HelpRows[i, 1];
            }

            // 가운데: 조준점
            var cross = Node("Crosshair", root, CC, CC, Vector2.zero, Vector2.zero);
            crosshair[0] = Box("Dot", cross, CC, CC, Vector2.zero, new Vector2(6f, 6f), Color.white);
            crosshair[0].sprite = circle;
            crosshair[0].type = UnityEngine.UI.Image.Type.Simple;
            crosshair[1] = Box("Up", cross, CC, CC, new Vector2(0f, 13f), new Vector2(2f, 9f), Color.white, false);
            crosshair[2] = Box("Down", cross, CC, CC, new Vector2(0f, -13f), new Vector2(2f, 9f), Color.white, false);
            crosshair[3] = Box("Left", cross, CC, CC, new Vector2(-13f, 0f), new Vector2(9f, 2f), Color.white, false);
            crosshair[4] = Box("Right", cross, CC, CC, new Vector2(13f, 0f), new Vector2(9f, 2f), Color.white, false);

            // 조준점 아래: 상호작용 안내
            promptPanel = Box("Prompt", root, CC, TC, new Vector2(0f, -78f), new Vector2(420f, 40f), PanelColor);
            promptText = Label("Text", promptPanel.transform, CC, CC, Vector2.zero, new Vector2(900f, 40f), 19, TextMain, TextAnchor.MiddleCenter);
            promptPanel.gameObject.SetActive(false);

            // 위 가운데: 알림
            var toastRoot = Node("Toasts", root, TC, TC, new Vector2(0f, -120f), Vector2.zero);
            for (int i = 0; i < toastPanels.Length; i++)
            {
                toastPanels[i] = Box("Toast" + i, toastRoot, TC, TC, new Vector2(0f, -i * 46f), new Vector2(300f, 38f), PanelColor);
                toastTexts[i] = Label("Text", toastPanels[i].transform, CC, CC, Vector2.zero, new Vector2(900f, 38f), 19, TextMain, TextAnchor.MiddleCenter, true);
                toastPanels[i].gameObject.SetActive(false);
            }

            // 가운데 큰 글자 (쓰러졌을 때)
            bigText = Label("BigMessage", root, CC, CC, new Vector2(0f, 190f), new Vector2(1400f, 70f), 46, Bad, TextAnchor.MiddleCenter, true, true);
            bigText.gameObject.SetActive(false);

            // 오른쪽: 상점
            shopRoot = Box("Shop", root, RC, RC, new Vector2(-36f, -10f), new Vector2(430f, 398f), PanelColor).gameObject;
            var shop = shopRoot.transform;
            Label("Title", shop, TL, TL, new Vector2(24f, -16f), new Vector2(150f, 32f), 26, TextMain, TextAnchor.MiddleLeft, true);
            LastLabel.text = "상점";
            shopCurrency = Label("Currency", shop, TR, TR, new Vector2(-24f, -16f), new Vector2(220f, 32f), 22, Gold, TextAnchor.MiddleRight, true);
            string[] keys = { "7", "8", "9", "0", "-" };
            for (int i = 0; i < keys.Length; i++)
            {
                var row = Box("Row" + i, shop, TL, TL, new Vector2(14f, -62f - i * 60f), new Vector2(402f, 52f), RowColor).transform;
                var badge = Box("Key", row, LC, LC, new Vector2(11f, 0f), new Vector2(30f, 30f), new Color(1f, 1f, 1f, 0.92f)).transform;
                var keyText = Label("KeyText", badge, CC, CC, Vector2.zero, new Vector2(30f, 30f), 19, new Color(0.08f, 0.09f, 0.12f), TextAnchor.MiddleCenter, true);
                keyText.text = keys[i];
                shopNames[i] = Label("Name", row, TL, TL, new Vector2(54f, -5f), new Vector2(230f, 24f), 19, TextMain, TextAnchor.MiddleLeft, true);
                shopSubs[i] = Label("Sub", row, TL, TL, new Vector2(54f, -28f), new Vector2(230f, 20f), 14, TextDim, TextAnchor.MiddleLeft);
                shopPrices[i] = Label("Price", row, RC, RC, new Vector2(-14f, 0f), new Vector2(120f, 30f), 24, Gold, TextAnchor.MiddleRight, true);
            }
            Label("Footer", shop, BC, BC, new Vector2(0f, 12f), new Vector2(400f, 22f), 15, TextDim, TextAnchor.MiddleCenter);
            LastLabel.text = "7 · 8 · 9 · 0 · - 키로 삽니다";
            shopRoot.SetActive(false);
        }

        UnityEngine.UI.Text InfoRow(Transform parent, float y, string name)
        {
            Label("RowLabel", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, y), new Vector2(170f, 30f), 17, TextDim, TextAnchor.MiddleLeft);
            LastLabel.text = name;
            return Label("RowValue", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, y), new Vector2(110f, 30f), 23, TextMain, TextAnchor.MiddleRight, true);
        }

        // ───────────────────────────────── 만들기 도구

        UnityEngine.UI.Text LastLabel;

        RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        UnityEngine.UI.Image Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color, bool roundedCorners = true)
        {
            var rt = Node(name, parent, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            if (roundedCorners)
            {
                img.sprite = rounded;
                img.type = UnityEngine.UI.Image.Type.Sliced;
            }
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>막대의 채워진 부분. anchorMax.x 를 0~1 로 바꿔 길이를 조절한다.</summary>
        RectTransform Fill(string name, Transform parent, Color color)
        {
            var img = Box(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, color);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        UnityEngine.UI.Text Label(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
                                  int fontSize, Color color, TextAnchor align, bool bold = false, bool shadow = false)
        {
            var rt = Node(name, parent, anchor, pivot, pos, size);
            var t = rt.gameObject.AddComponent<UnityEngine.UI.Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            if (shadow)
            {
                var s = rt.gameObject.AddComponent<UnityEngine.UI.Shadow>();
                s.effectColor = new Color(0f, 0f, 0f, 0.85f);
                s.effectDistance = new Vector2(1.5f, -1.5f);
            }
            LastLabel = t;
            return t;
        }

        /// <summary>모서리가 둥근 흰 사각형. 9분할로 늘려 써서 크기가 달라도 모서리가 일정하다. radius 가 절반이면 원이 된다.</summary>
        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Mathf.Clamp(fx, radius, size - radius), cy = Mathf.Clamp(fy, radius, size - radius);
                    float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            int border = Mathf.Min(radius, size / 2 - 1);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }

        /// <summary>속이 빈 고리 (조준 대상 표시).</summary>
        static Sprite MakeRing(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[size * size];
            float c = size * 0.5f, outer = c - 1f, inner = outer - thickness;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>가운데는 투명하고 가장자리로 갈수록 진해지는 그림 (피격 표시).</summary>
        static Sprite MakeVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01((Mathf.Sqrt(u * u + v * v) - 0.78f) / 0.62f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ───────────────────────────────── 갱신

        void LateUpdate()
        {
            if (canvas == null) return;
            if (cam == null) cam = Camera.main;
            var kb = Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame)
            {
                helpRoot.SetActive(!helpRoot.activeSelf);
                helpAutoHideAt = 0f;
            }
            if (helpAutoHideAt > 0f && Time.time > helpAutoHideAt)
            {
                helpRoot.SetActive(false);
                helpAutoHideAt = 0f;
            }

            UpdateVitals();
            UpdateSlots();
            UpdateInfo();
            UpdateCenter();
            UpdateToasts();
            UpdateShop();
            UpdateWorldLabels();
        }

        void UpdateVitals()
        {
            float hp = Mathf.Clamp01((float)player.Health / Mathf.Max(1, player.MaxHealth));
            healthFill.anchorMax = new Vector2(hp, 1f);
            staminaFill.anchorMax = new Vector2(player.Stamina01, 1f);
            if (player.Health != lastHealth)
            {
                lastHealth = player.Health;
                healthText.text = Mathf.Max(0, lastHealth).ToString();
                healthText.color = hp < 0.35f ? Bad : TextMain;
            }

            // 맞았을 때 잠깐, 체력이 낮을 때는 계속 화면 가장자리가 붉어진다
            float flash = Mathf.Clamp01((player.HitFlashUntil - Time.time) / 0.6f) * 0.7f;
            float low = hp < 0.35f ? 0.3f + Mathf.Sin(Time.time * 4f) * 0.1f : 0f;
            var c = vignetteImage.color;
            c.a = Mathf.Max(flash, low);
            vignetteImage.color = c;

            bool caught = Time.time < player.CaughtMessageUntil;
            if (bigText.gameObject.activeSelf != caught)
            {
                bigText.gameObject.SetActive(caught);
                if (caught) bigText.text = "쓰러졌습니다 — 착륙지로 돌아왔습니다";
            }
        }

        void UpdateSlots()
        {
            int tool = (int)player.Tool;
            bool carrying = player.Carried != null;
            bool decoyTicking = game.DecoyActive || game.DecoyCooldownLeft > 0f;
            bool changed = game.Version != lastSlotVersion || tool != lastTool || carrying != lastCarrying ||
                           (decoyTicking && Time.time >= nextSlotTick);
            if (!changed) return;
            lastSlotVersion = game.Version;
            lastCarrying = carrying;
            nextSlotTick = Time.time + 0.2f;

            if (tool != lastTool)
            {
                lastTool = tool;
                for (int i = 0; i < ToolNames.Length; i++)
                {
                    bool selected = i == tool;
                    slotPanels[i].color = selected ? PanelSelected : PanelColor;
                    slotRects[i].anchoredPosition = new Vector2(slotRects[i].anchoredPosition.x, selected ? 12f : 0f);
                    slotNames[i].color = selected ? TextMain : TextDim;
                }
            }

            SetCount(0, carrying ? "사용 불가" : game.Ammo + "발", carrying || game.Ammo <= 0);
            string markers = game.GuideMarkers + "개";
            SetCount(1, markers, game.GuideMarkers <= 0);
            SetCount(2, markers, game.GuideMarkers <= 0);
            SetCount(3, game.SafeZones + "개", game.SafeZones <= 0);
            SetCount(5, game.HidingSpots + "개", game.HidingSpots <= 0);

            var decoy = PrototypeSettings.Active.decoy;
            float fullWidth = slotRects[4].sizeDelta.x - 24f, progress = 1f;
            if (!game.HasDecoy) SetCount(4, "없음", true);
            else if (game.DecoyActive)
            {
                SetCount(4, "작동 " + Mathf.CeilToInt(game.DecoyTimeLeft) + "초", false);
                progress = game.DecoyTimeLeft / Mathf.Max(0.01f, decoy.duration);
            }
            else if (game.DecoyCooldownLeft > 0f)
            {
                SetCount(4, "대기 " + Mathf.CeilToInt(game.DecoyCooldownLeft) + "초", true);
                progress = 1f - game.DecoyCooldownLeft / Mathf.Max(0.01f, decoy.cooldown);
            }
            else SetCount(4, "준비됨", false);
            slotAccents[4].sizeDelta = new Vector2(fullWidth * Mathf.Clamp01(progress), 4f);
        }

        void SetCount(int i, string text, bool warn)
        {
            slotCounts[i].text = text;
            slotCounts[i].color = warn ? Bad : TextMain;
        }

        void UpdateInfo()
        {
            if (game.Currency != lastCurrency) { lastCurrency = game.Currency; currencyText.text = lastCurrency.ToString(); }
            int outside = director != null ? director.OutsideAlive : 0;
            int inZone = director != null ? director.InSafeZone : 0;
            if (outside != lastOutside) { lastOutside = outside; outsideText.text = outside + "명"; }
            if (inZone != lastInZone) { lastInZone = inZone; inZoneText.text = inZone + "명"; }
            if (player.FollowerCount != lastFollowers)
            {
                lastFollowers = player.FollowerCount;
                followerText.text = lastFollowers + "명";
                followerText.color = lastFollowers > 0 ? Gold : TextMain;
            }
            if (dungeon != null && (dungeon.seed != lastSeed || dungeon.rooms.Count != lastRooms))
            {
                lastSeed = dungeon.seed;
                lastRooms = dungeon.rooms.Count;
                mapText.text = "맵 시드 " + lastSeed + "   구역 " + lastRooms + "개      H  조작법";
            }
        }

        void UpdateCenter()
        {
            // 상호작용 안내
            string prompt = null;
            var aimed = player.AimedCivilian;
            var spot = player.AimedSpot;
            if (player.Carried != null) prompt = PromptDrop;
            else if (spot != null) prompt = !spot.IsOpen ? PromptOpen : spot.OccupantCount > 0 ? PromptTakeOut : PromptEmpty;
            else if (aimed != null) prompt = !aimed.IsAlive ? PromptCorpse : player.IsFollower(aimed) ? PromptUnfollow : PromptFollow;
            if (!ReferenceEquals(prompt, lastPrompt))
            {
                lastPrompt = prompt;
                ShowPill(promptPanel, promptText, prompt, TextMain);
            }

            var crossColor = player.HasAimTarget ? Good : Color.white;
            if (crosshair[0].color != crossColor)
                for (int i = 0; i < crosshair.Length; i++) crosshair[i].color = crossColor;

            // 조준하고 있는 대상 위에 고리를 그려, 지금 누구에게 상호작용하는지 보여 준다
            bool ringOn = player.HasAimTarget && cam != null;
            if (ringOn)
            {
                var sp = cam.WorldToScreenPoint(player.AimPoint);
                ringOn = sp.z > 0.1f;
                if (ringOn) aimRing.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y) / canvas.scaleFactor;
            }
            if (aimRing.gameObject.activeSelf != ringOn) aimRing.gameObject.SetActive(ringOn);

            // 지금 든 도구로 할 수 있는 일, 또는 할 수 없는 이유
            string status = null;
            bool warn = false;
            if (player.Carried != null)
                status = player.Carried.IsAlive ? "민간인을 들고 있습니다  ·  느려짐  ·  총 사용 불가" : "시체를 들고 있습니다  ·  느려짐  ·  총 사용 불가";
            else switch (player.Tool)
            {
                case ToolKind.Gun:
                    if (game.Ammo <= 0) { status = "탄약이 없습니다  ·  상점에서 살 수 있습니다"; warn = true; }
                    break;
                case ToolKind.ExitMarker:
                case ToolKind.SafeZoneMarker:
                    if (game.GuideMarkers <= 0) { status = "유도표시가 없습니다  ·  상점에서 살 수 있습니다"; warn = true; }
                    else if (player.PreviewValid) status = player.Tool == ToolKind.ExitMarker
                        ? "좌클릭: 탈출용 유도표시 붙이기  ·  화살표는 보는 방향" : "좌클릭: 안전지대용 유도표시 붙이기  ·  화살표는 보는 방향";
                    else status = "가까운 바닥이나 벽을 바라보면 붙일 수 있습니다";
                    break;
                case ToolKind.SafeZone:
                    if (game.SafeZones <= 0) { status = "안전지대가 없습니다  ·  상점에서 살 수 있습니다"; warn = true; }
                    else if (player.PreviewValid) status = "좌클릭: 지금 서 있는 자리에 안전지대 설치";
                    else { status = "여기에는 설치할 수 없습니다  ·  건물 안에서만 가능"; warn = true; }
                    break;
                case ToolKind.HidingSpot:
                    if (game.HidingSpots <= 0) { status = "숨을 곳이 없습니다  ·  상점에서 살 수 있습니다"; warn = true; }
                    else if (player.PreviewValid) status = "좌클릭: 앞 바닥에 숨을 곳 설치  ·  문이 내 쪽을 봅니다";
                    else { status = "여기에는 설치할 수 없습니다  ·  건물 안의 빈 바닥에만 가능"; warn = true; }
                    break;
                case ToolKind.Decoy:
                    if (!game.HasDecoy) { status = "디코이가 없습니다  ·  상점에서 살 수 있습니다"; warn = true; }
                    else if (game.DecoyActive) status = "디코이 작동 중  ·  범위 안의 적이 나를 쫓아옵니다";
                    else if (game.DecoyCooldownLeft > 0f) { status = "디코이를 아직 다시 쓸 수 없습니다"; warn = true; }
                    else status = "좌클릭: 디코이 사용  ·  범위 안의 적이 나를 쫓아옵니다";
                    break;
            }
            if (!ReferenceEquals(status, lastStatus))
            {
                lastStatus = status;
                ShowPill(statusPanel, statusText, status, warn ? Bad : TextMain);
            }
        }

        /// <summary>글자 길이에 맞춰 바탕 크기를 정한다. text 가 null 이면 숨긴다.</summary>
        void ShowPill(UnityEngine.UI.Image panel, UnityEngine.UI.Text label, string text, Color color)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
            if (!show) return;
            label.text = text;
            label.color = color;
            var rt = (RectTransform)panel.transform;
            rt.sizeDelta = new Vector2(label.preferredWidth + 44f, rt.sizeDelta.y);
        }

        void UpdateToasts()
        {
            if (game.MessageSerial != lastMessageSerial)
            {
                lastMessageSerial = game.MessageSerial;
                string msg = game.LastMessage;
                var color = msg.Contains("사망") || msg.Contains("없") || msg.Contains("부족") ? Bad : msg.Contains("+") ? Gold : TextMain;
                toasts.Insert(0, new Toast { text = msg, color = color, until = Time.time + 4f });
                if (toasts.Count > toastPanels.Length) toasts.RemoveAt(toasts.Count - 1);
                for (int i = 0; i < toasts.Count; i++) ShowPill(toastPanels[i], toastTexts[i], toasts[i].text, toasts[i].color);
            }

            for (int i = toasts.Count - 1; i >= 0; i--)
                if (Time.time >= toasts[i].until) toasts.RemoveAt(i);
            for (int i = 0; i < toastPanels.Length; i++)
            {
                bool show = i < toasts.Count;
                if (toastPanels[i].gameObject.activeSelf != show) toastPanels[i].gameObject.SetActive(show);
                if (!show) continue;
                // 사라지기 전 0.4초 동안 흐려진다
                float a = Mathf.Clamp01((toasts[i].until - Time.time) / 0.4f);
                var pc = PanelColor; pc.a *= a;
                toastPanels[i].color = pc;
                var tc = toasts[i].color; tc.a = a;
                toastTexts[i].color = tc;
            }
        }

        void UpdateShop()
        {
            bool near = game.IsNearShop(player.transform.position);
            if (shopRoot.activeSelf != near)
            {
                shopRoot.SetActive(near);
                lastShopVersion = -1;
            }
            if (!near || game.Version == lastShopVersion) return;
            lastShopVersion = game.Version;

            var shop = PrototypeSettings.Active.shop;
            shopCurrency.text = "재화 " + game.Currency;
            ShopRow(0, "탄약 " + shop.ammoPackSize + "발", "보유 " + game.Ammo + "발", shop.ammoPackPrice, false);
            ShopRow(1, "유도표시 1개", "보유 " + game.GuideMarkers + "개  ·  탈출용·안전지대용 공용", shop.guideMarkerPrice, false);
            ShopRow(2, "안전지대 1개", "보유 " + game.SafeZones + "개", shop.safeZonePrice, false);
            ShopRow(3, "디코이", game.HasDecoy ? "보유 중" : "한 번 사면 여러 번 쓸 수 있음", shop.decoyPrice, game.HasDecoy);
            ShopRow(4, "숨을 곳 1개", "보유 " + game.HidingSpots + "개  ·  민간인이 숨는 캐비닛", shop.hidingSpotPrice, false);
        }

        void ShopRow(int i, string name, string sub, int price, bool owned)
        {
            shopNames[i].text = name;
            shopSubs[i].text = sub;
            bool affordable = game.Currency >= price;
            shopPrices[i].text = owned ? "보유" : price.ToString();
            shopPrices[i].color = owned ? TextDim : affordable ? Gold : Bad;
            shopNames[i].color = owned || !affordable ? TextDim : TextMain;
        }

        /// <summary>민간인과 적의 머리 위에 상태 문구를 놓는다.</summary>
        void UpdateWorldLabels()
        {
            if (cam == null) cam = Camera.main;
            int used = 0;
            if (cam != null)
            {
                var s = PrototypeSettings.Active;
                if (s.civilian.showStateLabel)
                {
                    var list = CivilianAI.All;
                    for (int i = 0; i < list.Count && used < worldLabels.Length; i++)
                    {
                        var c = list[i];
                        if (c.IsCarried || !c.IsShown) continue; // 숨은 민간인은 숨을 곳을 열어야 보인다
                        if (PlaceLabel(used, c.transform.position + Vector3.up * (c.IsAlive ? 2.1f : 0.9f), c.StateLabel,
                                       c.IsAlive ? c.StateColor : TextDim, s.civilian.labelDistance)) used++;
                    }
                }
                if (s.enemy.showStateLabel)
                {
                    var list = EnemyAI.All;
                    for (int i = 0; i < list.Count && used < worldLabels.Length; i++)
                    {
                        var e = list[i];
                        var color = e.State == EnemyState.Chase || e.State == EnemyState.Catch ? Bad :
                                    e.State == EnemyState.Stunned ? new Color(0.4f, 0.95f, 1f) :
                                    e.State == EnemyState.Investigate || e.State == EnemyState.Search ? Gold : TextMain;
                        if (PlaceLabel(used, e.transform.position + Vector3.up * 2.1f, e.StateLabel, color, 25f)) used++;
                    }
                }
            }
            for (int i = used; i < worldLabels.Length; i++)
                if (worldLabels[i].gameObject.activeSelf) worldLabels[i].gameObject.SetActive(false);
        }

        bool PlaceLabel(int index, Vector3 worldPos, string text, Color color, float maxDistance)
        {
            var sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z < 0.2f || sp.z > maxDistance) return false;
            var label = worldLabels[index];
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            label.rectTransform.anchoredPosition = new Vector2(sp.x, sp.y) / canvas.scaleFactor;
            label.text = text;
            label.color = color;
            return true;
        }
    }
}
