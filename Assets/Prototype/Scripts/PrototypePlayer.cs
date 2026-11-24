using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace DungeonProto
{
    /// <summary>플레이어가 손에 든 도구. 숫자 키 1~6 이나 마우스 휠로 고른다.</summary>
    public enum ToolKind { Gun, ExitMarker, SafeZoneMarker, SafeZone, Decoy, HidingSpot }

    /// <summary>
    /// 프로토타입용 1인칭 컨트롤러. 걷기/달리기(스태미너)/웅크리기/점프/손전등에 더해
    /// 민간인·숨을 곳 상호작용, 총, 유도표시·안전지대·숨을 곳 설치, 디코이, 체력을 다룬다.
    /// 속도·체력 같은 수치는 PrototypeSettings 에서 바꾼다. 재화와 물품 수는 PrototypeGame 이 관리한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PrototypePlayer : MonoBehaviour
    {
        const int ToolCount = 6;
        /// <summary>플레이어의 몸이 놓이는 레이어 (기본 제공되는 Ignore Raycast).</summary>
        public const int SelfLayer = 2;
        /// <summary>
        /// 플레이어에서 쏘는 판정선에 쓰는 마스크. 자기 몸은 뺀다.
        /// 카메라가 몸 안에 있어도, 시선을 아래로 내리면 판정선이 자기 몸에 먼저 걸리기 때문이다.
        /// </summary>
        public const int RayMask = ~(1 << SelfLayer);
        const int rayMask = RayMask;

        public Transform cameraRoot;
        public Light flashlight;

        [Header("고정 값 (게임 수치는 PrototypeSettings 에셋에서)")]
        public float gravity = -22f;
        public float standHeight = 1.8f;
        public float crouchHeight = 1.1f;
        public float grabDistance = 3f;
        public float holdDistance = 1.8f;

        CharacterController cc;
        float pitch, vy, stamina;
        bool crouching, sprinting;
        float moveInput;
        float stepDistance; // 다음 발소리까지 걸은 거리

        // 체력
        public int Health { get; private set; }
        public int MaxHealth => S.player.maxHealth;
        public float Stamina01 => Mathf.Clamp01(stamina / Mathf.Max(0.01f, S.player.staminaMax));
        public int TimesCaught { get; private set; }
        public float CaughtMessageUntil { get; private set; }
        public float HitFlashUntil { get; private set; }

        // 도구
        public ToolKind Tool { get; private set; } = ToolKind.Gun;
        float nextFire;
        bool previewValid;
        /// <summary>지금 든 도구를 이 자리에 쓸 수 있는지 (유도표시·안전지대·디코이·숨을 곳). 화면 표시가 안내 문구를 고를 때 쓴다.</summary>
        public bool PreviewValid => previewValid;
        GuideMarker.Placement previewPlacement;
        Vector3 previewZoneCenter, previewSpotPos;
        Quaternion previewSpotRot;
        GameObject[] markerGhosts;
        GameObject spotGhost;
        RangeIndicator ring;
        LineRenderer tracer;
        float tracerUntil;

        // 민간인 · 숨을 곳
        readonly List<CivilianAI> followers = new List<CivilianAI>();
        Transform carryAnchor;
        public CivilianAI Carried { get; private set; }
        /// <summary>지금 조준하고 있는 민간인 (조준 여유 각도 안에서 조준선에 가장 가까운 것).</summary>
        public CivilianAI AimedCivilian { get; private set; }
        /// <summary>지금 조준하고 있는 숨을 곳.</summary>
        public HidingSpot AimedSpot { get; private set; }
        public bool HasAimTarget => AimedCivilian != null || AimedSpot != null;
        /// <summary>조준하고 있는 대상의 위치. 화면 표시가 대상 위에 표시를 그릴 때 쓴다.</summary>
        public Vector3 AimPoint { get; private set; }
        public int FollowerCount => followers.Count;
        public bool IsFollower(CivilianAI c) => followers.Contains(c);

        // 물체 잡기 (예전 루팅 시험용, 오른쪽 버튼)
        Rigidbody held;
        Collider heldCol;

        public bool IsCrouching => crouching;

        PrototypeSettings S => PrototypeSettings.Active;
        PrototypeGame Game => PrototypeGame.Instance;

        /// <summary>적이 들을 수 있는 반경. 웅크리거나 멈춰 있으면 0.</summary>
        public float NoiseRadius(float sprintRange, float walkRange)
        {
            if (crouching || moveInput < 0.1f || !cc.isGrounded) return 0f;
            return sprinting ? sprintRange : walkRange;
        }

        // ───────────────────────────────── 체력 · 잡힘

        /// <summary>적에게 잡힘 → 체력이 줄고, 0 이 되면 착륙지로 돌아간다.</summary>
        public void TakeHit(int damage, EnemyAI by)
        {
            Health -= damage;
            HitFlashUntil = Time.time + 0.6f;
            ProtoAudio.Play2D(ProtoSound.PlayerHit, 0.9f);
            if (Health <= 0) Caught(by);
        }

        /// <summary>체력이 다함 → 들고 있던 것을 놓고 착륙지로 돌아간다. 따라오던 민간인은 그 자리에서 자율 행동으로 돌아간다.</summary>
        public void Caught(EnemyAI by)
        {
            ReleaseLoot();
            if (Carried != null) DropCarried(true);
            for (int i = followers.Count - 1; i >= 0; i--)
            {
                var f = followers[i];
                followers.RemoveAt(i);
                if (f != null) f.StopFollowing();
            }

            TimesCaught++;
            CaughtMessageUntil = Time.time + 2.5f;
            ProtoAudio.Play2D(ProtoSound.PlayerDown, 0.8f);
            Health = MaxHealth;
            var spawn = SpawnMarker.FindFirst(SpawnKind.PlayerSpawn);
            if (spawn == null) return;
            cc.enabled = false;
            transform.SetPositionAndRotation(spawn.transform.position, Quaternion.Euler(0f, spawn.transform.eulerAngles.y, 0f));
            cc.enabled = true;
            vy = 0f;
        }

        // ───────────────────────────────── 민간인 · 숨을 곳

        /// <summary>줄지어 따라오게 하기 위한 앞사람. 첫 번째는 플레이어, 그다음은 바로 앞의 민간인.</summary>
        public Transform LeaderOf(CivilianAI c)
        {
            int i = followers.IndexOf(c);
            return i <= 0 || followers[i - 1] == null ? transform : followers[i - 1].transform;
        }

        /// <summary>민간인이 사라졌을 때(구출·사망) 민간인 쪽에서 부른다.</summary>
        public void Forget(CivilianAI c)
        {
            followers.Remove(c);
            if (Carried == c) Carried = null;
            if (AimedCivilian == c) AimedCivilian = null;
        }

        void ToggleFollow(CivilianAI c)
        {
            if (followers.Remove(c))
            {
                c.StopFollowing();
                ProtoAudio.Play2D(ProtoSound.FollowOff, 0.5f);
                return;
            }
            followers.Add(c);
            c.StartFollowing(this);
            ProtoAudio.Play2D(ProtoSound.FollowOn, 0.5f);
        }

        void Carry(CivilianAI c)
        {
            followers.Remove(c);
            c.BeginCarry(this, carryAnchor);
            Carried = c;
            AimedCivilian = null;
            ProtoAudio.Play2D(ProtoSound.FollowOn, 0.5f, 0.8f);
        }

        void DropCarried(bool forced)
        {
            var c = Carried;
            if (c == null) return;
            var dropPos = transform.position + transform.forward * 0.9f;
            if (c.EndCarry(dropPos, forced))
            {
                Carried = null;
                if (!forced) ProtoAudio.Play2D(ProtoSound.FollowOff, 0.5f, 0.8f);
            }
            else if (Game != null)
            {
                Game.Toast("여기에는 내려놓을 수 없습니다");
                ProtoAudio.Play2D(ProtoSound.Deny, 0.5f);
            }
        }

        /// <summary>숨을 곳: 닫혀 있으면 연다. 열려 있고 안에 민간인이 있으면 데리고(또는 들고) 나온다. 비어 있으면 닫는다.</summary>
        void InteractSpot(HidingSpot spot, bool carry)
        {
            if (!spot.IsOpen) { spot.SetOpen(true, this); return; }
            var c = spot.FirstOccupant;
            if (c == null) { spot.SetOpen(false, this); return; }
            if (carry) Carry(c);
            else ToggleFollow(c);
        }

        /// <summary>
        /// 조준할 대상을 고른다. 조준선에 정확히 닿지 않아도, 조준 여유 각도 안에서 조준선에 가장 가까운 것을 고른다.
        /// 벽 너머에 있는 것은 고르지 않는다.
        /// </summary>
        void UpdateAim()
        {
            AimedCivilian = null;
            AimedSpot = null;
            if (Carried != null) return;

            var origin = cameraRoot.position;
            var forward = cameraRoot.forward;
            float best = float.MaxValue;

            // 조준선이 숨을 곳에 직접 닿으면 그것을 고른다 (캐비닛은 커서, 한가운데를 겨누지 않아도 된다)
            if (Physics.Raycast(origin, forward, out var direct, S.player.interactDistance, rayMask, QueryTriggerInteraction.Ignore))
            {
                var hitSpot = direct.collider.GetComponentInParent<HidingSpot>();
                if (hitSpot != null)
                {
                    AimedSpot = hitSpot;
                    AimPoint = hitSpot.AimPoint;
                    best = direct.distance * 0.5f;
                }
            }

            var spots = HidingSpot.All;
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                if (!AimScore(origin, forward, s.AimPoint, s, out float score) || score >= best) continue;
                best = score;
                AimedSpot = s;
                AimPoint = s.AimPoint;
            }

            var civilians = CivilianAI.All;
            for (int i = 0; i < civilians.Count; i++)
            {
                var c = civilians[i];
                if (c.IsHidden || c.IsCarried) continue; // 숨은 민간인은 숨을 곳을 통해서만 만난다
                var basePos = c.transform.position;
                // 서 있는 민간인은 다리·몸통·머리, 쓰러진 시체는 바닥 가까이를 본다
                int points = c.IsAlive ? 3 : 1;
                for (int k = 0; k < points; k++)
                {
                    var p = basePos + Vector3.up * (c.IsAlive ? 0.45f + k * 0.55f : 0.3f);
                    if (!AimScore(origin, forward, p, null, out float score) || score >= best) continue;
                    best = score;
                    AimedCivilian = c;
                    AimedSpot = null;
                    AimPoint = p;
                }
            }
        }

        /// <summary>point 를 조준할 수 있으면 점수(낮을수록 조준선에 가깝다)를 낸다. own 은 point 를 감싼 숨을 곳.</summary>
        bool AimScore(Vector3 origin, Vector3 forward, Vector3 point, HidingSpot own, out float score)
        {
            score = 0f;
            var P = S.player;
            var to = point - origin;
            float d = to.magnitude;
            if (d > P.interactDistance || d < 0.01f) return false;
            float angle = Vector3.Angle(forward, to);
            // 바로 앞에 있는 것은 넉넉하게 잡아 준다
            float limit = d < 1.4f ? Mathf.Max(P.interactAngle, 35f) : P.interactAngle;
            if (angle > limit) return false;
            // 가로막혀 있으면 고르지 않는다 (민간인의 충돌체는 트리거라 가로막지 않는다)
            if (Physics.Raycast(origin, to / d, out var hit, d - 0.05f, rayMask, QueryTriggerInteraction.Ignore) &&
                (own == null || hit.collider.GetComponentInParent<HidingSpot>() != own)) return false;
            score = angle + d * 0.5f;
            return true;
        }

        // ───────────────────────────────── 시작

        void Start()
        {
            cc = GetComponent<CharacterController>();
            gameObject.layer = SelfLayer; // 적과 민간인의 시야 판정(모든 레이어)에는 그대로 걸린다
            stamina = S.player.staminaMax;
            Health = MaxHealth;

            var spawn = SpawnMarker.FindFirst(SpawnKind.PlayerSpawn);
            if (spawn != null)
            {
                cc.enabled = false;
                transform.SetPositionAndRotation(spawn.transform.position, Quaternion.Euler(0f, spawn.transform.eulerAngles.y, 0f));
                cc.enabled = true;
            }

            if (GetComponent<NavMeshModifier>() == null) gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            // 든 민간인을 붙이는 자리
            carryAnchor = new GameObject("CarryAnchor").transform;
            carryAnchor.SetParent(transform, false);
            carryAnchor.localPosition = new Vector3(0.55f, 0.85f, 0.6f);

            markerGhosts = new[]
            {
                GuideMarker.BuildVisual(GuideMarkerKind.Exit, true),
                GuideMarker.BuildVisual(GuideMarkerKind.SafeZone, true),
            };
            foreach (var g in markerGhosts) g.SetActive(false);

            // 숨을 곳을 놓을 자리 미리보기: 바닥의 발자국과 문 쪽 표시
            spotGhost = new GameObject("HidingSpotPreview");
            var ghostMat = ProtoVisuals.Lit(new Color(0.7f, 0.85f, 1f), true);
            ProtoVisuals.Primitive(PrimitiveType.Cube, "Footprint", spotGhost.transform, new Vector3(0f, 0.02f, 0f),
                new Vector3(HidingSpot.Width, 0.03f, HidingSpot.Depth), ghostMat);
            ProtoVisuals.Primitive(PrimitiveType.Cube, "DoorSide", spotGhost.transform, new Vector3(0f, 0.05f, HidingSpot.Depth * 0.5f + 0.12f),
                new Vector3(HidingSpot.Width * 0.6f, 0.03f, 0.12f), ghostMat);
            spotGhost.SetActive(false);

            ring = RangeIndicator.Create("RangeIndicator");

            var tracerGo = new GameObject("GunTracer");
            tracer = tracerGo.AddComponent<LineRenderer>();
            tracer.sharedMaterial = ProtoVisuals.Line();
            tracer.positionCount = 2;
            tracer.widthMultiplier = 0.03f;
            tracer.startColor = tracer.endColor = new Color(1f, 0.95f, 0.5f);
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.enabled = false;

            SetCursorLocked(true);
        }

        void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ───────────────────────────────── 매 프레임

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            float dt = Time.deltaTime;
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            var P = S.player;

            if (tracer.enabled && Time.time >= tracerUntil) tracer.enabled = false;

            if (kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            if (!locked && mouse.leftButton.wasPressedThisFrame) { SetCursorLocked(true); return; }

            if (locked)
            {
                Vector2 d = mouse.delta.ReadValue() * P.mouseSensitivity;
                transform.Rotate(0f, d.x, 0f);
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
                cameraRoot.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }

            // 웅크리기 (머리 위가 막혀 있으면 일어서지 않음)
            if (kb.cKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame)
            {
                if (!crouching) crouching = true;
                else if (!Physics.Raycast(transform.position + Vector3.up * crouchHeight, Vector3.up, standHeight - crouchHeight + 0.1f, rayMask, QueryTriggerInteraction.Ignore))
                    crouching = false;
            }
            cc.height = Mathf.MoveTowards(cc.height, crouching ? crouchHeight : standHeight, dt * 6f);
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            cameraRoot.localPosition = new Vector3(0f, cc.height - 0.15f, 0f);

            // 이동
            var mv = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                                 (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            mv = Vector2.ClampMagnitude(mv, 1f);
            bool sprint = kb.leftShiftKey.isPressed && !crouching && mv.y > 0f && stamina > 0.05f;
            sprinting = sprint;
            moveInput = mv.magnitude;
            stamina = sprint ? stamina - dt : Mathf.Min(P.staminaMax, stamina + dt * P.staminaRecover);
            float speed = crouching ? P.crouchSpeed : sprint ? P.sprintSpeed : P.walkSpeed;
            if (Carried != null) speed *= S.rescue.carrySpeedMultiplier; // 들고 있으면 느려진다
            if (held != null) speed *= 0.8f;
            var move = (transform.right * mv.x + transform.forward * mv.y) * speed;

            if (cc.isGrounded)
            {
                if (vy < 0f) vy = -2f;
                if (kb.spaceKey.wasPressedThisFrame && !crouching) vy = Mathf.Sqrt(P.jumpHeight * -2f * gravity);
            }
            vy += gravity * dt;
            move.y = vy;
            cc.Move(move * dt);

            // 발소리: 적이 듣는 소리와 같은 조건이다 (웅크리거나 멈춰 있으면 나지 않는다)
            if (cc.isGrounded && moveInput > 0.1f && !crouching)
            {
                stepDistance += speed * moveInput * dt;
                if (stepDistance >= (sprint ? 1.7f : 1.35f))
                {
                    stepDistance = 0f;
                    if (S.audio.playerFootsteps) ProtoAudio.Play2D(ProtoSound.PlayerStep, sprint ? 0.45f : 0.28f, Random.Range(0.92f, 1.08f));
                }
            }

            if (kb.fKey.wasPressedThisFrame && flashlight != null) flashlight.enabled = !flashlight.enabled;

            // 도구 고르기
            if (kb.digit1Key.wasPressedThisFrame) Tool = ToolKind.Gun;
            if (kb.digit2Key.wasPressedThisFrame) Tool = ToolKind.ExitMarker;
            if (kb.digit3Key.wasPressedThisFrame) Tool = ToolKind.SafeZoneMarker;
            if (kb.digit4Key.wasPressedThisFrame) Tool = ToolKind.SafeZone;
            if (kb.digit5Key.wasPressedThisFrame) Tool = ToolKind.Decoy;
            if (kb.digit6Key.wasPressedThisFrame) Tool = ToolKind.HidingSpot;
            // 마우스 휠로도 도구를 바꾼다 (아래로 굴리면 다음 칸)
            float wheel = mouse.scroll.ReadValue().y;
            if (locked && Mathf.Abs(wheel) > 0.01f) Tool = (ToolKind)(((int)Tool + (wheel < 0f ? 1 : ToolCount - 1)) % ToolCount);

            // 상호작용
            UpdateAim();
            if (kb.eKey.wasPressedThisFrame)
            {
                if (AimedSpot != null) InteractSpot(AimedSpot, false);
                else if (AimedCivilian != null && AimedCivilian.IsAlive) ToggleFollow(AimedCivilian);
            }
            if (kb.gKey.wasPressedThisFrame)
            {
                if (Carried != null) DropCarried(false);
                else if (AimedSpot != null) InteractSpot(AimedSpot, true);
                else if (AimedCivilian != null) Carry(AimedCivilian);
            }

            // 상점 (착륙지에서만)
            var game = Game;
            if (game != null && game.IsNearShop(transform.position))
            {
                if (kb.digit7Key.wasPressedThisFrame) game.TryBuy(ShopItem.Ammo);
                if (kb.digit8Key.wasPressedThisFrame) game.TryBuy(ShopItem.GuideMarker);
                if (kb.digit9Key.wasPressedThisFrame) game.TryBuy(ShopItem.SafeZone);
                if (kb.digit0Key.wasPressedThisFrame) game.TryBuy(ShopItem.Decoy);
                if (kb.minusKey.wasPressedThisFrame) game.TryBuy(ShopItem.HidingSpot);
            }

            UpdatePreview();
            if (locked && mouse.leftButton.wasPressedThisFrame && game != null) UseTool(game);

            // 물체 잡기 (오른쪽 버튼)
            var cam = cameraRoot;
            if (locked && mouse.rightButton.wasPressedThisFrame && held == null &&
                Physics.Raycast(cam.position, cam.forward, out var lootHit, grabDistance, rayMask, QueryTriggerInteraction.Ignore) &&
                lootHit.rigidbody != null && lootHit.rigidbody.GetComponent<SpawnMarker>() != null)
                GrabLoot(lootHit.rigidbody);
            if (mouse.rightButton.wasReleasedThisFrame && held != null) ReleaseLoot();
        }

        // ───────────────────────────────── 도구

        /// <summary>설치 전 미리보기: 놓일 자리의 모양과, 도구가 미치는 범위 원을 항상 보여 준다.</summary>
        void UpdatePreview()
        {
            previewValid = false;
            bool showRing = false, showSpot = false;
            Vector3 ringCenter = default;
            float ringRadius = 0f;
            int ghost = -1;
            var cam = cameraRoot;

            switch (Tool)
            {
                case ToolKind.ExitMarker:
                case ToolKind.SafeZoneMarker:
                    // 바라보는 곳의 바닥에 붙인다. 벽이나 물건을 보고 있으면 그 앞 바닥이 된다
                    if (GuideMarker.TryGetFloorPlacement(cam.position, cam.forward, transform.forward, transform.position.y,
                            S.guideMarker.placeDistance, rayMask, out previewPlacement))
                    {
                        previewValid = true;
                        ghost = Tool == ToolKind.ExitMarker ? 0 : 1;
                        markerGhosts[ghost].transform.SetPositionAndRotation(
                            previewPlacement.point + previewPlacement.normal * 0.03f, previewPlacement.Rotation);
                        showRing = true;
                        ringCenter = previewPlacement.navPoint;
                        ringRadius = S.guideMarker.recognitionDistance;
                    }
                    break;

                case ToolKind.SafeZone:
                    showRing = true;
                    ringRadius = S.safeZone.radius;
                    if (NavMesh.SamplePosition(transform.position, out var nav, 1f, NavMesh.AllAreas))
                    {
                        previewValid = SafeZone.FindContaining(nav.position) == null;
                        previewZoneCenter = nav.position;
                        ringCenter = nav.position;
                    }
                    else ringCenter = transform.position; // 걸을 수 있는 곳(건물 안)이 아니면 설치할 수 없다
                    break;

                case ToolKind.Decoy:
                    if (S.decoy.range > 0f)
                    {
                        showRing = true;
                        ringCenter = transform.position;
                        ringRadius = S.decoy.range;
                        var game = Game;
                        previewValid = game != null && game.HasDecoy && game.DecoyCooldownLeft <= 0f;
                    }
                    break;

                case ToolKind.HidingSpot:
                {
                    // 플레이어 앞 바닥에, 문이 플레이어 쪽을 보도록 놓는다
                    var forward = transform.forward;
                    var target = transform.position + forward * S.hiding.placeDistance;
                    if (NavMesh.SamplePosition(target, out var spotNav, 0.8f, NavMesh.AllAreas) &&
                        Mathf.Abs(spotNav.position.y - transform.position.y) < 0.5f &&
                        Physics.Raycast(spotNav.position + Vector3.up * 0.5f, Vector3.down, out var floor, 1.2f, rayMask, QueryTriggerInteraction.Ignore))
                    {
                        showSpot = true;
                        previewSpotPos = floor.point;
                        previewSpotRot = Quaternion.LookRotation(-forward);
                        // 벽이나 물건, 사람과 겹치면 놓을 수 없다
                        var half = new Vector3(HidingSpot.Width, HidingSpot.Height, HidingSpot.Depth) * 0.5f - Vector3.one * 0.04f;
                        previewValid = !Physics.CheckBox(floor.point + Vector3.up * (HidingSpot.Height * 0.5f + 0.06f), half, previewSpotRot,
                            rayMask, QueryTriggerInteraction.Ignore);
                        spotGhost.transform.SetPositionAndRotation(previewSpotPos, previewSpotRot);
                    }
                    break;
                }
            }

            for (int i = 0; i < markerGhosts.Length; i++)
                if (markerGhosts[i].activeSelf != (i == ghost)) markerGhosts[i].SetActive(i == ghost);
            if (spotGhost.activeSelf != showSpot) spotGhost.SetActive(showSpot);

            var R = S.rangeIndicator;
            if (showRing && R.show) ring.Show(ringCenter, ringRadius, previewValid ? R.validColor : R.invalidColor);
            else ring.Hide();
        }

        void UseTool(PrototypeGame game)
        {
            switch (Tool)
            {
                case ToolKind.Gun:
                    if (Carried != null) { Refuse(game, "들고 있는 동안에는 쏠 수 없습니다"); return; }
                    if (Time.time < nextFire) return;
                    if (!game.TryUseAmmo()) return;
                    nextFire = Time.time + S.gun.fireInterval;
                    Fire(game);
                    break;

                case ToolKind.ExitMarker:
                case ToolKind.SafeZoneMarker:
                    if (!previewValid) { Refuse(game, "여기에는 붙일 수 없습니다"); return; }
                    if (!game.TryUseGuideMarker()) return;
                    GuideMarker.Place(Tool == ToolKind.ExitMarker ? GuideMarkerKind.Exit : GuideMarkerKind.SafeZone, previewPlacement);
                    ProtoAudio.Play2D(ProtoSound.PlaceMarker, 0.6f);
                    break;

                case ToolKind.SafeZone:
                    if (!previewValid) { Refuse(game, "여기에는 설치할 수 없습니다"); return; }
                    if (!game.TryUseSafeZone()) return;
                    SafeZone.Place(previewZoneCenter, S.safeZone.radius);
                    ProtoAudio.Play2D(ProtoSound.PlaceZone, 0.6f);
                    break;

                case ToolKind.Decoy:
                    game.TryActivateDecoy(transform);
                    break;

                case ToolKind.HidingSpot:
                    if (!previewValid) { Refuse(game, "여기에는 설치할 수 없습니다"); return; }
                    if (!game.TryUseHidingSpot()) return;
                    HidingSpot.Create(previewSpotPos, previewSpotRot);
                    ProtoAudio.Play2D(ProtoSound.PlaceSpot, 0.7f);
                    break;
            }
        }

        /// <summary>도구를 쓸 수 없을 때: 이유를 알리고 거절음을 낸다.</summary>
        static void Refuse(PrototypeGame game, string message)
        {
            game.Toast(message);
            ProtoAudio.Play2D(ProtoSound.Deny, 0.5f);
        }

        /// <summary>비살상 총. 맞은 적은 설정한 시간 동안 무력화된다.</summary>
        void Fire(PrototypeGame game)
        {
            var cam = cameraRoot;
            var G = S.gun;
            var end = cam.position + cam.forward * G.range;
            if (Physics.Raycast(cam.position, cam.forward, out var hit, G.range, rayMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                var enemy = hit.collider.GetComponentInParent<EnemyAI>();
                if (enemy != null) enemy.Stun(G.stunSeconds);
            }
            tracer.SetPosition(0, cam.position + cam.right * 0.2f - cam.up * 0.2f + cam.forward * 0.4f);
            tracer.SetPosition(1, end);
            tracer.enabled = true;
            tracerUntil = Time.time + 0.06f;
            ProtoAudio.Play2D(ProtoSound.GunShot, 0.8f, Random.Range(0.95f, 1.05f));
            game.EmitNoise(transform.position, G.noiseRadius);
        }

        // ───────────────────────────────── 물체 잡기 (예전 루팅 시험용)

        void FixedUpdate()
        {
            if (held == null) return;
            var target = cameraRoot.position + cameraRoot.forward * holdDistance;
            var delta = target - held.position;
            if (delta.magnitude > 2.5f) { ReleaseLoot(); return; }
            held.linearVelocity = delta * 14f;
            held.angularVelocity *= 0.85f;
        }

        void GrabLoot(Rigidbody rb)
        {
            held = rb;
            heldCol = rb.GetComponent<Collider>();
            if (heldCol != null) Physics.IgnoreCollision(heldCol, cc, true);
            held.useGravity = false;
        }

        void ReleaseLoot()
        {
            if (held != null)
            {
                held.useGravity = true;
                if (heldCol != null) Physics.IgnoreCollision(heldCol, cc, false);
            }
            held = null;
            heldCol = null;
        }
    }
}
