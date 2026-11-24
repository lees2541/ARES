using System;
using UnityEngine;

namespace DungeonProto
{
    /// <summary>프로토타입에서 쓰는 소리의 종류.</summary>
    public enum ProtoSound
    {
        EnemyStep, EnemyAlert, EnemySpotted, EnemyInvestigate, EnemyCatch, EnemyStun, EnemyGrowlLoop,
        PlayerStep, PlayerHit, PlayerDown, Heartbeat,
        GunShot, Deny,
        CivilianScream, CivilianDeath, FollowOn, FollowOff,
        Rescue, SelfEscape, CorpseRetrieved, Buy,
        PlaceMarker, PlaceZone, PlaceSpot, DecoyOn, DecoyTick,
        DoorOpen, DoorClose, CabinetRattle, HiddenBreathLoop,
    }

    /// <summary>
    /// 프로토타입용 소리. 소리 파일이 없어서 모든 소리를 코드로 만들어(합성) 쓴다. 상황을 알리기 위한 임시 소리다.
    /// 씬에 놓지 않아도 처음 쓸 때 스스로 만들어진다. 음량과 들리는 거리는 PrototypeSettings 의 "소리"에서 바꾼다.
    ///  - PlayAt: 월드의 한 지점에서 나는 소리 (거리에 따라 작아진다)
    ///  - Play2D: 플레이어에게 바로 들리는 소리 (알림, 자기 발소리)
    ///  - AttachLoop: 대상에 붙어 계속 나는 소리 (음량은 붙인 쪽이 조절한다)
    /// 쫓길 때의 심장 소리와 디코이 작동음은 여기서 직접 낸다.
    /// </summary>
    public class ProtoAudio : MonoBehaviour
    {
        const int SampleRate = 44100;
        const int PoolSize = 24;
        const float TwoPi = Mathf.PI * 2f;

        static ProtoAudio instance;
        static readonly AudioClip[] clips = new AudioClip[Enum.GetValues(typeof(ProtoSound)).Length];
        static float lastSpottedAt = -99f;

        AudioSource[] pool;
        int next;
        Transform listener;
        float nextTensionCheck, nextBeatAt, nextDecoyTick;
        float chaserDistance = float.MaxValue;

        static PrototypeSettings.AudioConfig A => PrototypeSettings.Active.audio;

        /// <summary>전체 음량. 계속 나는 소리의 음량을 정할 때 곱한다.</summary>
        public static float Master => A.masterVolume;

        // ───────────────────────────────── 밖에서 부르는 것

        /// <summary>소리 관리자를 미리 만들어 둔다 (심장 소리처럼 스스로 내는 소리가 있어서).</summary>
        public static void EnsureExists()
        {
            if (Application.isPlaying) GetInstance();
        }

        /// <summary>position 에서 나는 소리. maxDistance 밖에서는 들리지 않는다.</summary>
        public static void PlayAt(ProtoSound sound, Vector3 position, float volume, float maxDistance, float pitch = 1f)
        {
            if (!Application.isPlaying || maxDistance <= 0f) return;
            float v = volume * A.masterVolume;
            if (v <= 0f) return;

            var host = GetInstance();
            // 들리지 않는 거리의 소리는 재생하지 않는다
            if (host.TryGetListener(out var ear) && (position - ear).sqrMagnitude > maxDistance * maxDistance) return;

            var src = host.Take();
            src.transform.position = position;
            src.spatialBlend = 1f;
            src.minDistance = Mathf.Min(2f, maxDistance * 0.2f);
            src.maxDistance = maxDistance;
            src.pitch = pitch;
            src.volume = v;
            src.clip = Clip(sound);
            src.Play();
        }

        /// <summary>거리와 상관없이 플레이어에게 들리는 소리.</summary>
        public static void Play2D(ProtoSound sound, float volume, float pitch = 1f)
        {
            if (!Application.isPlaying) return;
            float v = volume * A.masterVolume;
            if (v <= 0f) return;

            var src = GetInstance().Take();
            src.spatialBlend = 0f;
            src.pitch = pitch;
            src.volume = v;
            src.clip = Clip(sound);
            src.Play();
        }

        /// <summary>
        /// 적이 플레이어를 발견했을 때의 경고음. 적이 보이지 않아도 들린다.
        /// 여러 적이 한꺼번에 발견해도 겹쳐 울리지 않게 간격을 둔다.
        /// </summary>
        public static void PlaySpotted()
        {
            if (!Application.isPlaying || !A.spottedWarning) return;
            if (Time.time - lastSpottedAt < 2.5f) return;
            lastSpottedAt = Time.time;
            Play2D(ProtoSound.EnemySpotted, 0.9f);
        }

        /// <summary>parent 에 붙어 계속 나는 소리를 만든다. 음량 0 으로 시작하며, 붙인 쪽이 volume 을 조절한다.</summary>
        public static AudioSource AttachLoop(ProtoSound sound, Transform parent, Vector3 localPosition, float maxDistance)
        {
            if (!Application.isPlaying) return null;
            var go = new GameObject("Loop_" + sound);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var src = go.AddComponent<AudioSource>();
            Configure(src);
            src.clip = Clip(sound);
            src.loop = true;
            src.spatialBlend = 1f;
            src.minDistance = 1.5f;
            src.maxDistance = Mathf.Max(2f, maxDistance);
            src.volume = 0f;
            src.Play();
            return src;
        }

        // ───────────────────────────────── 관리

        static ProtoAudio GetInstance()
        {
            if (instance == null)
            {
                var go = new GameObject("ProtoAudio");
                instance = go.AddComponent<ProtoAudio>();
            }
            return instance;
        }

        void Awake()
        {
            pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var child = new GameObject("OneShot_" + i);
                child.transform.SetParent(transform, false);
                pool[i] = child.AddComponent<AudioSource>();
                Configure(pool[i]);
            }
            // 처음 쫓길 때 멈칫하지 않도록 소리를 미리 만들어 둔다
            for (int i = 0; i < clips.Length; i++) Clip((ProtoSound)i);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        static void Configure(AudioSource src)
        {
            src.playOnAwake = false;
            src.dopplerLevel = 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
        }

        /// <summary>쉬고 있는 재생기를 고른다. 모두 쓰는 중이면 가장 오래된 것을 다시 쓴다.</summary>
        AudioSource Take()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                int k = (next + i) % PoolSize;
                if (pool[k].isPlaying) continue;
                next = (k + 1) % PoolSize;
                return pool[k];
            }
            var oldest = pool[next];
            next = (next + 1) % PoolSize;
            return oldest;
        }

        bool TryGetListener(out Vector3 position)
        {
            if (listener == null)
            {
                var found = FindAnyObjectByType<AudioListener>();
                if (found != null) listener = found.transform;
            }
            position = listener != null ? listener.position : Vector3.zero;
            return listener != null;
        }

        void Update()
        {
            var a = A;

            // 쫓길 때의 심장 소리: 플레이어를 쫓는 적이 가까울수록 빠르고 크게
            if (a.heartbeat && a.masterVolume > 0f)
            {
                if (Time.time >= nextTensionCheck)
                {
                    nextTensionCheck = Time.time + 0.1f;
                    chaserDistance = NearestChaserDistance();
                }
                if (chaserDistance < a.enemyChaseHearDistance && Time.time >= nextBeatAt)
                {
                    float near = Mathf.InverseLerp(a.enemyChaseHearDistance, 3f, chaserDistance);
                    Play2D(ProtoSound.Heartbeat, Mathf.Lerp(0.35f, 1f, near));
                    nextBeatAt = Time.time + Mathf.Lerp(1.05f, 0.42f, near);
                }
            }

            var game = PrototypeGame.Instance;
            if (game != null && game.DecoyActive && Time.time >= nextDecoyTick)
            {
                nextDecoyTick = Time.time + 0.5f;
                Play2D(ProtoSound.DecoyTick, 0.3f);
            }
        }

        float NearestChaserDistance()
        {
            if (!TryGetListener(out var ear)) return float.MaxValue;
            float best = float.MaxValue;
            var enemies = EnemyAI.All;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (!enemies[i].IsChasingPlayer) continue;
                float d = (enemies[i].transform.position - ear).sqrMagnitude;
                if (d < best) best = d;
            }
            return best < float.MaxValue ? Mathf.Sqrt(best) : float.MaxValue;
        }

        // ───────────────────────────────── 소리 만들기

        static AudioClip Clip(ProtoSound sound)
        {
            int i = (int)sound;
            if (clips[i] != null) return clips[i];

            bool loop = sound == ProtoSound.EnemyGrowlLoop || sound == ProtoSound.HiddenBreathLoop;
            var data = Synthesize(sound);
            Normalize(data, 0.85f);
            if (!loop) FadeEnds(data);
            var clip = AudioClip.Create("Proto_" + sound, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            clips[i] = clip;
            return clip;
        }

        static float[] Synthesize(ProtoSound sound)
        {
            float[] d;
            switch (sound)
            {
                case ProtoSound.EnemyStep:
                    d = NewBuffer(0.22f);
                    Tone(d, 0f, 0.2f, 75f, 40f, 0.9f, 0, 0.002f, 3f);
                    Noise(d, 0f, 0.06f, 0.35f, 0.15f, 3f, 11);
                    return d;

                case ProtoSound.EnemyAlert: // 추격 시작: 거칠게 올라가는 소리
                    d = NewBuffer(0.9f);
                    Tone(d, 0f, 0.9f, 80f, 190f, 0.6f, 1, 0.03f, 1f, 9f, 0.06f);
                    Tone(d, 0f, 0.9f, 121f, 287f, 0.3f, 1, 0.03f, 1f, 9f, 0.06f);
                    Noise(d, 0f, 0.9f, 0.25f, 0.3f, 1.5f, 31, 0.05f);
                    return d;

                case ProtoSound.EnemySpotted: // 들켰다: 어긋난 두 음이 부딪치는 경고음
                    d = NewBuffer(1.3f);
                    Tone(d, 0f, 1.3f, 196f, 196f, 0.45f, 1, 0.004f, 2.2f);
                    Tone(d, 0f, 1.3f, 207.65f, 207.65f, 0.45f, 1, 0.004f, 2.2f);
                    Tone(d, 0f, 1.3f, 98f, 98f, 0.5f, 0, 0.004f, 1.8f);
                    Tone(d, 0f, 0.5f, 880f, 830f, 0.25f, 2, 0.002f, 3f);
                    Noise(d, 0f, 0.25f, 0.5f, 0.6f, 3f, 41);
                    return d;

                case ProtoSound.EnemyInvestigate:
                    d = NewBuffer(0.35f);
                    Tone(d, 0f, 0.3f, 110f, 160f, 0.6f, 1, 0.02f, 1.5f);
                    return d;

                case ProtoSound.EnemyCatch:
                    d = NewBuffer(0.45f);
                    Noise(d, 0f, 0.4f, 0.9f, 0.5f, 2f, 51);
                    Tone(d, 0f, 0.4f, 90f, 45f, 0.8f, 1, 0.002f, 2f);
                    return d;

                case ProtoSound.EnemyStun:
                    d = NewBuffer(0.4f);
                    Tone(d, 0f, 0.35f, 1100f, 180f, 0.5f, 2, 0.002f, 1.5f);
                    Noise(d, 0f, 0.2f, 0.3f, 0.9f, 3f, 61);
                    return d;

                case ProtoSound.EnemyGrowlLoop:
                    return GrowlLoop();

                case ProtoSound.PlayerStep:
                    d = NewBuffer(0.14f);
                    Noise(d, 0f, 0.1f, 0.5f, 0.25f, 3f, 21);
                    Tone(d, 0f, 0.1f, 130f, 80f, 0.35f, 0, 0.002f, 3f);
                    return d;

                case ProtoSound.PlayerHit:
                    d = NewBuffer(0.3f);
                    Tone(d, 0f, 0.28f, 160f, 60f, 0.9f, 0, 0.002f, 2f);
                    Noise(d, 0f, 0.12f, 0.6f, 0.4f, 3f, 71);
                    return d;

                case ProtoSound.PlayerDown:
                    d = NewBuffer(1.1f);
                    Tone(d, 0f, 1f, 330f, 70f, 0.6f, 0, 0.01f, 1.2f);
                    Tone(d, 0f, 1f, 220f, 47f, 0.4f, 1, 0.01f, 1.2f);
                    return d;

                case ProtoSound.Heartbeat:
                    d = NewBuffer(0.5f);
                    Tone(d, 0f, 0.16f, 62f, 45f, 1f, 0, 0.004f, 2f);
                    Tone(d, 0.2f, 0.18f, 55f, 40f, 0.75f, 0, 0.004f, 2f);
                    return d;

                case ProtoSound.GunShot:
                    d = NewBuffer(0.35f);
                    Noise(d, 0f, 0.3f, 1f, 0.85f, 4f, 81, 0.001f);
                    Tone(d, 0f, 0.2f, 220f, 60f, 0.7f, 0, 0.001f, 3f);
                    return d;

                case ProtoSound.Deny:
                    d = NewBuffer(0.22f);
                    Tone(d, 0f, 0.09f, 140f, 140f, 0.5f, 2, 0.002f, 0.6f);
                    Tone(d, 0.11f, 0.1f, 105f, 105f, 0.5f, 2, 0.002f, 0.8f);
                    return d;

                case ProtoSound.CivilianScream:
                    d = NewBuffer(0.62f);
                    Tone(d, 0f, 0.3f, 600f, 950f, 0.55f, 1, 0.02f, 0.3f, 14f, 0.05f);
                    Tone(d, 0.3f, 0.3f, 950f, 700f, 0.55f, 1, 0.01f, 0.9f, 14f, 0.05f);
                    return d;

                case ProtoSound.CivilianDeath:
                    d = NewBuffer(0.72f);
                    Tone(d, 0f, 0.7f, 700f, 180f, 0.6f, 1, 0.01f, 1f, 11f, 0.08f);
                    Noise(d, 0f, 0.3f, 0.3f, 0.5f, 2f, 91);
                    return d;

                case ProtoSound.FollowOn:
                    d = NewBuffer(0.24f);
                    Tone(d, 0f, 0.1f, 660f, 660f, 0.4f, 0, 0.003f, 1.5f);
                    Tone(d, 0.1f, 0.12f, 990f, 990f, 0.4f, 0, 0.003f, 1.5f);
                    return d;

                case ProtoSound.FollowOff:
                    d = NewBuffer(0.24f);
                    Tone(d, 0f, 0.1f, 990f, 990f, 0.4f, 0, 0.003f, 1.5f);
                    Tone(d, 0.1f, 0.12f, 660f, 660f, 0.4f, 0, 0.003f, 1.5f);
                    return d;

                case ProtoSound.Rescue: // 구출: 올라가는 화음
                    d = NewBuffer(0.85f);
                    Tone(d, 0f, 0.5f, 523.25f, 523.25f, 0.35f, 0, 0.004f, 2.5f);
                    Tone(d, 0.09f, 0.5f, 659.25f, 659.25f, 0.35f, 0, 0.004f, 2.5f);
                    Tone(d, 0.18f, 0.5f, 783.99f, 783.99f, 0.35f, 0, 0.004f, 2.5f);
                    Tone(d, 0.27f, 0.55f, 1046.5f, 1046.5f, 0.35f, 0, 0.004f, 2.5f);
                    return d;

                case ProtoSound.SelfEscape:
                    d = NewBuffer(0.55f);
                    Tone(d, 0f, 0.4f, 659.25f, 659.25f, 0.3f, 0, 0.004f, 2.5f);
                    Tone(d, 0.1f, 0.42f, 783.99f, 783.99f, 0.3f, 0, 0.004f, 2.5f);
                    return d;

                case ProtoSound.CorpseRetrieved:
                    d = NewBuffer(0.62f);
                    Tone(d, 0f, 0.6f, 392f, 392f, 0.4f, 0, 0.004f, 2.5f);
                    Tone(d, 0f, 0.6f, 196f, 196f, 0.25f, 0, 0.004f, 2f);
                    return d;

                case ProtoSound.Buy:
                    d = NewBuffer(0.3f);
                    Tone(d, 0f, 0.12f, 1318.5f, 1318.5f, 0.3f, 0, 0.002f, 2f);
                    Tone(d, 0.08f, 0.2f, 1760f, 1760f, 0.3f, 0, 0.002f, 2.5f);
                    return d;

                case ProtoSound.PlaceMarker:
                    d = NewBuffer(0.12f);
                    Noise(d, 0f, 0.05f, 0.5f, 0.7f, 3f, 101);
                    Tone(d, 0f, 0.1f, 900f, 700f, 0.3f, 0, 0.002f, 3f);
                    return d;

                case ProtoSound.PlaceZone:
                    d = NewBuffer(0.5f);
                    Tone(d, 0f, 0.45f, 220f, 660f, 0.45f, 0, 0.02f, 1f);
                    Tone(d, 0f, 0.45f, 330f, 990f, 0.2f, 0, 0.02f, 1f);
                    return d;

                case ProtoSound.PlaceSpot:
                    d = NewBuffer(0.3f);
                    Tone(d, 0f, 0.28f, 95f, 60f, 0.9f, 0, 0.002f, 2.5f);
                    Noise(d, 0f, 0.1f, 0.5f, 0.3f, 3f, 111);
                    return d;

                case ProtoSound.DecoyOn:
                    d = NewBuffer(0.4f);
                    for (int k = 0; k < 3; k++) Tone(d, k * 0.13f, 0.09f, 1000f, 1000f, 0.3f, 2, 0.002f, 0.5f);
                    return d;

                case ProtoSound.DecoyTick:
                    d = NewBuffer(0.08f);
                    Tone(d, 0f, 0.06f, 1400f, 1400f, 0.25f, 2, 0.002f, 0.6f);
                    return d;

                case ProtoSound.DoorOpen: // 철문이 삐걱거리며 열림
                    d = NewBuffer(0.4f);
                    Tone(d, 0f, 0.38f, 280f, 420f, 0.35f, 1, 0.02f, 1f, 23f, 0.12f);
                    Noise(d, 0f, 0.1f, 0.4f, 0.5f, 3f, 121);
                    return d;

                case ProtoSound.DoorClose:
                    d = NewBuffer(0.3f);
                    Noise(d, 0f, 0.12f, 0.9f, 0.6f, 3f, 131);
                    Tone(d, 0f, 0.28f, 180f, 170f, 0.5f, 0, 0.002f, 3f);
                    Tone(d, 0f, 0.2f, 733f, 720f, 0.15f, 0, 0.002f, 4f);
                    return d;

                case ProtoSound.CabinetRattle: // 안에서 문이 덜컹거림
                    d = NewBuffer(0.48f);
                    for (int k = 0; k < 5; k++)
                    {
                        Noise(d, k * 0.08f, 0.04f, 0.6f, 0.7f, 3f, 140 + k);
                        Tone(d, k * 0.08f, 0.06f, 520f + 60f * k, 500f, 0.25f, 0, 0.002f, 3f);
                    }
                    return d;

                default: // HiddenBreathLoop
                    return BreathLoop();
            }
        }

        static float[] NewBuffer(float seconds) => new float[Mathf.CeilToInt(seconds * SampleRate)];

        /// <summary>
        /// 음높이가 f0 에서 f1 로 바뀌는 음을 더한다. shape: 0 사인, 1 톱니, 2 사각.
        /// attack 초 동안 커졌다가, decay 가 클수록 빨리 작아진다. vibHz·vibDepth 는 떨림.
        /// </summary>
        static void Tone(float[] d, float start, float duration, float f0, float f1, float amp, int shape,
            float attack, float decay, float vibHz = 0f, float vibDepth = 0f)
        {
            int s0 = (int)(start * SampleRate), n = (int)(duration * SampleRate);
            double phase = 0.0;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = (float)i / n;
                float time = (float)i / SampleRate;
                float f = Mathf.Lerp(f0, f1, t);
                if (vibHz > 0f) f *= 1f + vibDepth * Mathf.Sin(TwoPi * vibHz * time);
                phase += f / SampleRate;
                float p = (float)(phase - Math.Floor(phase));
                float w = shape == 0 ? Mathf.Sin(TwoPi * p) : shape == 1 ? 2f * p - 1f : p < 0.5f ? 1f : -1f;
                float env = Mathf.Min(1f, time / attack) * Mathf.Pow(1f - t, decay);
                d[s0 + i] += w * amp * env;
            }
        }

        /// <summary>잡음을 더한다. brightness 가 작을수록 먹먹하다 (1 이면 그대로).</summary>
        static void Noise(float[] d, float start, float duration, float amp, float brightness, float decay, int seed, float attack = 0.002f)
        {
            int s0 = (int)(start * SampleRate), n = (int)(duration * SampleRate);
            var rng = new System.Random(seed);
            float y = 0f;
            float gain = amp / Mathf.Sqrt(Mathf.Clamp(brightness, 0.02f, 1f)); // 먹먹하게 하면 작아지는 만큼 키운다
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = (float)i / n;
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                y += brightness * (x - y);
                float env = Mathf.Min(1f, (float)i / SampleRate / attack) * Mathf.Pow(1f - t, decay);
                d[s0 + i] += y * gain * env;
            }
        }

        /// <summary>낮게 으르렁거리는 소리. 끝과 처음이 이어지도록 모든 성분이 2초에 정수 번 반복된다.</summary>
        static float[] GrowlLoop()
        {
            var d = NewBuffer(2f);
            float y = 0f;
            // 걸러 낸 값이 이어지도록 두 바퀴 돌리고, 두 번째 바퀴의 값을 쓴다
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / SampleRate;
                    float saw1 = 2f * Mathf.Repeat(55f * t, 1f) - 1f;
                    float saw2 = 2f * Mathf.Repeat(82.5f * t, 1f) - 1f;
                    float sub = Mathf.Sin(TwoPi * 27.5f * t);
                    float tremolo = 0.55f + 0.3f * Mathf.Sin(TwoPi * 6f * t) + 0.15f * Mathf.Sin(TwoPi * t);
                    float x = (saw1 * 0.3f + saw2 * 0.18f + sub * 0.35f) * tremolo;
                    y += 0.12f * (x - y);
                    if (pass == 1) d[i] = y;
                }
            }
            return d;
        }

        /// <summary>숨죽인 숨소리. 들숨과 날숨 사이가 조용해서 끝과 처음이 이어진다.</summary>
        static float[] BreathLoop()
        {
            var d = NewBuffer(3f);
            var rng = new System.Random(151);
            float y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SampleRate;
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                float env = 0f, brightness = 0.1f;
                if (t < 1.1f) { env = Mathf.Sin(Mathf.PI * t / 1.1f); brightness = 0.14f; }                          // 들숨
                else if (t > 1.45f && t < 2.75f) { env = 0.8f * Mathf.Sin(Mathf.PI * (t - 1.45f) / 1.3f); brightness = 0.07f; } // 날숨
                y += brightness * (x - y);
                d[i] = y * env * env;
            }
            return d;
        }

        static void Normalize(float[] d, float peak)
        {
            float max = 0f;
            for (int i = 0; i < d.Length; i++) max = Mathf.Max(max, Mathf.Abs(d[i]));
            if (max < 1e-5f) return;
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        /// <summary>끝에서 딱 소리가 나지 않게 마지막을 짧게 줄인다.</summary>
        static void FadeEnds(float[] d)
        {
            int n = Mathf.Min(d.Length / 2, SampleRate / 200);
            for (int i = 0; i < n; i++) d[d.Length - 1 - i] *= (float)i / n;
        }
    }
}
