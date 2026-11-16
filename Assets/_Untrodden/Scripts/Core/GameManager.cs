using Unity.XR.CoreUtils;
using Untrodden.Audio;
using Untrodden.Bears;
using Untrodden.Items;
using Untrodden.Player;
using Untrodden.UI;
using Untrodden.Weather;
using Untrodden.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Untrodden
{
    /// <summary>
    /// Builds the run and drives it: title → crossing (storms, death and respawn) → arrival.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        enum State { Title, Playing, Dead, Won }
        enum Storm { Calm, Warning, Blizzard }

        [SerializeField] GameAssets m_Assets;
        [SerializeField] WeatherController m_Weather;
        [SerializeField] XROrigin m_Origin;

        [Header("Storms (seconds)")]
        [SerializeField] Vector2 m_FirstStorm = new Vector2(100f, 140f);
        [SerializeField] Vector2 m_StormGap = new Vector2(140f, 200f);
        [SerializeField] float m_WarningSeconds = 30f;
        [SerializeField] float m_BlizzardSeconds = 70f;

        State m_State;
        Storm m_Storm;
        float m_StormPhaseEnd;
        float m_NextStormAt;
        System.Random m_Rng;

        ChunkStreamer m_Streamer;
        BearSpawner m_Bears;
        Belt m_Belt;
        MenuPanel m_Menu;
        ScreenEffects m_Fx;
        TeleportationProvider m_Teleport;

        Vector3 m_Checkpoint;
        bool m_HasCampfire;
        int m_Respawns;
        float m_Distance;
        Vector3 m_LastHead;

        public static GameManager Instance { get; private set; }
        public GameAssets Assets => m_Assets;
        public WorldMap World { get; private set; }
        public SurvivalState Survival { get; private set; }
        public ItemFactory Items { get; private set; }
        public BulletPouch Pouch => m_Belt.Pouch;
        public Rifle PlayerRifle { get; private set; }
        public Transform Head { get; private set; }
        public Vector3 Mouth => Head.position - Head.up * 0.07f + Head.forward * 0.05f;
        public bool IsPlaying => m_State == State.Playing;
        public float PlayTime { get; private set; }
        public bool StormWarning => m_Storm == Storm.Warning;
        public bool StormActive => m_Storm == Storm.Blizzard;
        public float StormCountdown => Mathf.Max(0f, m_StormPhaseEnd - PlayTime);

        void Awake()
        {
            Instance = this;
            if (m_Assets == null)
            {
                Debug.LogError("[Untrodden] GameManager has no GameAssets. Run Untrodden > Build Game Scene.");
                enabled = false;
                return;
            }
            MenuPanel.Font = m_Assets.font; // Korean-capable font for every UI text built below
            if (m_Assets.font == null)
                Debug.LogWarning("[Untrodden] GameAssets has no Korean font. Run Untrodden > Build Game Scene again.");

            var seed = GameSession.NextSeed ?? System.Environment.TickCount;
            GameSession.NextSeed = null;
            m_Rng = new System.Random(seed);

            World = new WorldMap(seed);
            Debug.Log($"[Untrodden] World seed {seed}. Start {World.StartPosition}, village {World.VillageXZ}.");

            if (m_Origin == null)
                m_Origin = FindFirstObjectByType<XROrigin>();
            Head = m_Origin.Camera.transform;
            m_Teleport = m_Origin.GetComponentInChildren<TeleportationProvider>(true);

            // Put the player at the start facing north (towards the sea).
            m_Origin.transform.SetPositionAndRotation(World.StartPosition, Quaternion.identity);
            m_Checkpoint = World.StartPosition;

            Items = new ItemFactory(m_Assets);

            var worldRoot = new GameObject("World").transform;
            m_Streamer = worldRoot.gameObject.AddComponent<ChunkStreamer>();
            m_Streamer.Init(World, m_Assets, Head, Items);
            m_Streamer.LoadImmediate(World.StartPosition, 1);
            WorldDressing.BuildSea(worldRoot, m_Assets);
            WorldDressing.BuildBounds(worldRoot);
            WorldDressing.BuildVillage(worldRoot, World, m_Assets);

            if (m_Weather != null)
            {
                m_Weather.AutoCycle = false;
                m_Weather.SetTarget(0f, 0f);
            }

            Survival = gameObject.AddComponent<SurvivalState>();
            Survival.Init(World, m_Weather, Head, m_Origin.transform);
            Survival.Died += OnDied;

            m_Belt = Belt.Create(Head, m_Assets);
            PlayerRifle = (Rifle)Items.Create(ItemKind.Rifle, m_Belt.RifleSocket.transform.position, Quaternion.identity);
            var steel = Items.Create(ItemKind.FireSteel, m_Belt.SteelSocket.transform.position, Quaternion.identity);
            var compass = Items.Create(ItemKind.Compass, m_Belt.CompassSocket.transform.position, Quaternion.identity);
            StartCoroutine(m_Belt.Equip(m_Belt.RifleSocket, PlayerRifle));
            StartCoroutine(m_Belt.Equip(m_Belt.SteelSocket, steel));
            StartCoroutine(m_Belt.Equip(m_Belt.CompassSocket, compass));

            var left = Meshes.FindDeep(m_Origin.transform, "Left Controller");
            var right = Meshes.FindDeep(m_Origin.transform, "Right Controller");
            if (left != null)
            {
                HandScoop.Attach(left, m_Assets);
                WristDisplay.Create(left).Init(Survival);
            }
            if (right != null)
                HandScoop.Attach(right, m_Assets);

            m_Fx = ScreenEffects.Create(m_Origin.Camera, Survival, m_Assets);

            m_Bears = new GameObject("Bears").AddComponent<BearSpawner>();
            m_Bears.Init(World, Head, m_Assets);

            gameObject.AddComponent<AmbientAudio>().Init(World, m_Weather, Head);

            m_Menu = MenuPanel.Create(m_Origin.Camera);
        }

        void Start()
        {
            m_LastHead = Head.position;
            if (GameSession.SkipTitle)
            {
                GameSession.SkipTitle = false;
                BeginRun();
            }
            else
            {
                ShowTitle();
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (m_State != State.Playing)
                return;

            PlayTime += Time.deltaTime;

            var step = Head.position - m_LastHead;
            step.y = 0f;
            if (step.magnitude < 20f)
                m_Distance += step.magnitude;
            m_LastHead = Head.position;

            UpdateStorms();

            if (World.IsInsideVillage(Head.position))
                Win();
        }

        // ------------------------------------------------------------------ flow

        void ShowTitle()
        {
            m_State = State.Title;
            Survival.Paused = true;
            m_Menu.Show(Head, Strings.TitleName,
                string.Format(Strings.TitleBody, m_Assets.creatorName),
                (Strings.Start, BeginRun));
        }

        void BeginRun()
        {
            m_Menu.Hide();
            m_State = State.Playing;
            Survival.Paused = false;
            m_Storm = Storm.Calm;
            m_NextStormAt = Range(m_FirstStorm);
            m_LastHead = Head.position;
        }

        void OnDied(Cause cause)
        {
            m_State = State.Dead;
            m_Bears.ClearAll();
            m_Fx.FadeTo(0.5f, 1.5f);

            string reason;
            switch (cause)
            {
                case Cause.Hunger: reason = Strings.DeathHunger; break;
                case Cause.Thirst: reason = Strings.DeathThirst; break;
                case Cause.Bear: reason = Strings.DeathBear; break;
                default: reason = Strings.DeathCold; break;
            }

            var where = m_HasCampfire ? Strings.ResumeCampfire : Strings.ResumeStart;
            m_Menu.Show(Head, Strings.DeathTitle, reason, (where, Respawn));
        }

        void Respawn()
        {
            m_Menu.Hide();
            m_Respawns++;
            var target = m_Checkpoint;
            target.y = World.Height(target.x, target.z) + 0.05f;
            m_Streamer.LoadImmediate(target, 1);
            TeleportTo(target);
            Survival.Respawn();
            m_Fx.FadeTo(0f, 1.2f);
            m_State = State.Playing;
            Survival.Paused = false;
            m_LastHead = target;
        }

        void Win()
        {
            m_State = State.Won;
            Survival.Paused = true;
            m_Bears.ClearAll();
            Sounds.Play(SoundId.Arrive, Head.position, 1f);

            var t = Mathf.FloorToInt(PlayTime);
            m_Menu.Show(Head, Strings.WinTitle,
                string.Format(Strings.WinBody, t / 60, t % 60, m_Respawns, m_Distance / 1000f, World.Seed),
                (Strings.NewForest, () => Reload(null)),
                (Strings.SameForest, () => Reload(World.Seed)));
        }

        void Reload(int? seed)
        {
            GameSession.NextSeed = seed ?? System.Environment.TickCount;
            GameSession.SkipTitle = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void SetCheckpoint(FirePit pit)
        {
            // Stand a step away from the fire, on the side facing the player.
            var away = Head.position - pit.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
                away = Vector3.back;
            m_Checkpoint = pit.transform.position + away.normalized * 1.5f;
            m_HasCampfire = true;
        }

        void TeleportTo(Vector3 feet)
        {
            if (m_Teleport != null)
            {
                m_Teleport.QueueTeleportRequest(new TeleportRequest
                {
                    destinationPosition = feet,
                    matchOrientation = MatchOrientation.None,
                });
            }
            else
            {
                m_Origin.transform.position = feet;
            }
        }

        // ------------------------------------------------------------------ storms

        void UpdateStorms()
        {
            if (m_Weather == null)
                return;

            switch (m_Storm)
            {
                case Storm.Calm:
                    if (PlayTime >= m_NextStormAt)
                    {
                        m_Storm = Storm.Warning;
                        m_StormPhaseEnd = PlayTime + m_WarningSeconds;
                        m_Weather.SetTarget(0.35f, 10f);
                        Sounds.Play(SoundId.Warning, Head.position + Head.forward * 5f, 1f);
                    }
                    break;

                case Storm.Warning:
                    if (PlayTime >= m_StormPhaseEnd)
                    {
                        m_Storm = Storm.Blizzard;
                        m_StormPhaseEnd = PlayTime + m_BlizzardSeconds;
                        m_Weather.SetTarget(1f, 6f);
                    }
                    break;

                case Storm.Blizzard:
                    if (PlayTime >= m_StormPhaseEnd)
                    {
                        m_Storm = Storm.Calm;
                        m_Weather.SetTarget(0f, 12f);
                        m_NextStormAt = PlayTime + Range(m_StormGap);
                    }
                    break;
            }
        }

        float Range(Vector2 r) => r.x + (float)m_Rng.NextDouble() * (r.y - r.x);
    }
}
