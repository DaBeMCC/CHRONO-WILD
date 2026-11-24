using System;
using System.Collections.Generic;
using ChronoWild.Core;
using ChronoWild.Health;
using ChronoWild.Player;
using ChronoWild.Story;
using ChronoWild.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChronoWild.Survival
{
    /// <summary>
    /// The survival loop on the player: needs (hunger, thirst, stamina, energy), gathering,
    /// carrying logs, blueprint construction, tools and weapons, food and saving by sleeping.
    /// Split across partial files by topic (Carry, Structures, Tools, Food, Save).
    /// </summary>
    [DefaultExecutionOrder(60)]
    [RequireComponent(typeof(DebugFirstPersonController), typeof(GameStateController), typeof(PlayerHealth))]
    public sealed partial class SurvivalRun : MonoBehaviour
    {
        internal enum Supply { Wood, Stone, Fiber, Shoots, Food, Water, RawMeat, CookedMeat, DirtyWater, SpoiledMeat }
        internal enum Structure { Fire, Shelter, Collector, LogWall, LogHolder, Cabin }

        internal readonly struct Recipe
        {
            public readonly Structure Kind;
            public readonly string Name, Description, Preview;
            public readonly int Logs, Wood, Stone, Fiber;

            public Recipe(Structure kind, string name, string description, string preview, int logs, int wood, int stone, int fiber)
            {
                Kind = kind;
                Name = name;
                Description = description;
                Preview = preview;
                Logs = logs;
                Wood = wood;
                Stone = stone;
                Fiber = fiber;
            }

            public int Total => Logs + Wood + Stone + Fiber;
            public int Need(int material) => material switch { 0 => Logs, 1 => Wood, 2 => Stone, _ => Fiber };
        }

        internal static readonly Recipe[] Recipes =
        {
            new Recipe(Structure.Fire, "Fogón", "Asa carne y brotes, hierve agua sucia y da luz por la noche.", "firepit", 0, 3, 3, 0),
            new Recipe(Structure.Shelter, "Refugio", "Duerme aquí para pasar la noche y guardar la partida.", "shelter", 0, 5, 0, 3),
            new Recipe(Structure.Collector, "Recolector de lluvia", "Almacena hasta 3 porciones de agua limpia cuando llueve.", "collector", 0, 3, 2, 2),
            new Recipe(Structure.LogWall, "Pared de troncos", "Tramo de 3 m para cercar el campamento frente a los raptores.", "logwall", 4, 4, 0, 0),
            new Recipe(Structure.LogHolder, "Leñera", "Guarda hasta 12 troncos para construir sin ir y venir del bosque.", "logholder", 2, 4, 0, 0),
            new Recipe(Structure.Cabin, "Cabaña", "Refugio sólido: dormir recupera toda la energía y guarda la partida.", "cabin", 12, 6, 0, 4),
        };

        internal static readonly string[] MaterialNames = { "Troncos", "Madera", "Piedra", "Fibra" };
        const int StackLimit = 30;
        const float SeaLevel = .6f;
        const float RestHunger = .18f, RestThirst = .24f;

        /// <summary>True while the player holds a lit torch (raptors keep their distance).</summary>
        public static bool TorchLit { get; private set; }

        [SerializeField] ResourcePickup[] pickups = Array.Empty<ResourcePickup>();
        [SerializeField] Material woodMaterial, stoneMaterial, fiberMaterial, waterMaterial;
        [SerializeField] Shader previewShader;
        [SerializeField] Material flameTemplate;

        readonly int[] inventory = { 0, 0, 0, 0, 1, 1, 0, 0, 0, 0 };
        readonly List<CampPiece> camp = new List<CampPiece>();
        readonly List<ResourcePickup> dropPickups = new List<ResourcePickup>();
        GameStateController states;
        PlayerHealth health;
        DebugFirstPersonController movement;
        ThirdPersonView thirdPerson;
        ResourcePickup nearbyPickup, nearbyLog;
        CampPiece nearbyCamp;
        bool nearbyWater;
        float damageIn, lastRest;
        bool hasRested, exhausted;

        internal float Hunger { get; private set; } = 1;
        internal float Hydration { get; private set; } = 1;
        internal float Stamina { get; private set; } = 1;
        internal float Energy { get; private set; } = .9f;
        internal string Hint { get; private set; }
        internal string Notice { get; private set; }
        internal float NoticeUntil { get; private set; }
        internal bool HasBlueprint => blueprint.HasValue;
        internal int Count(Supply supply) => inventory[(int)supply];
        /// <summary>Raised when the book (B) should open the backpack on the blueprint page.</summary>
        internal event Action<int> OpenPage;

        void Awake()
        {
            states = GetComponent<GameStateController>();
            health = GetComponent<PlayerHealth>();
            movement = GetComponent<DebugFirstPersonController>();
            thirdPerson = GetComponent<ThirdPersonView>();
            states.backpackEnabled = true;
            Carcass.MeatDropped += SpawnMeat;
        }

        void Start()
        {
            if (LoadRequested)
            {
                LoadRequested = false;
                StartCoroutine(LoadAfterStart());
            }
        }

        void Update()
        {
            Hint = null;
            if (IntroCinematic.Playing || health.IsDead || states.State != GameState.Playing)
            {
                HidePlacement();
                UpdateTools(null, Vector3.zero, false);
                UpdateCarry(false);
                // The world keeps running while the backpack is open (only pause stops time),
                // so hunger, thirst and spoilage must not freeze there either.
                if (!IntroCinematic.Playing && !health.IsDead && states.State == GameState.Backpack)
                    UpdateNeeds(Time.deltaTime, false);
                return;
            }

            float dt = Time.deltaTime;
            UpdateNeeds(dt, movement.IsRunning);
            if (health.IsDead)
                return;

            var eye = movement.cameraPivot;
            if (eye == null)
                return;

            var keyboard = Keyboard.current;
            if (keyboard?.bKey.wasPressedThisFrame == true)
            {
                states.SetState(GameState.Backpack);
                OpenPage?.Invoke(0);
                return;
            }
            if (keyboard?.gKey.wasPressedThisFrame == true)
                DropLog();

            Vector3 origin = thirdPerson != null && thirdPerson.isActiveAndEnabled ? thirdPerson.InteractionOrigin : eye.position;
            UpdateCarry(true);
            UpdateTools(eye, origin, true);
            if (blueprint.HasValue)
            {
                UpdatePlacement(origin, eye.forward);
                return;
            }

            // The story owns E when a machine component is in pickup range.
            if (MissionDirector.Instance != null && MissionDirector.Instance.PartInReach)
                return;

            FindTarget(origin, eye.forward);
            if (keyboard?.eKey.wasPressedThisFrame == true)
            {
                if (nearbyCamp != null)
                    UseCamp(nearbyCamp);
                else if (nearbyLog != null)
                    PickUpLog(nearbyLog);
                else if (nearbyPickup != null)
                    Gather(nearbyPickup);
                else if (nearbyWater)
                    FillDirtyWater();
            }
            if (keyboard?.cKey.wasPressedThisFrame == true && nearbyCamp != null && !nearbyCamp.Complete)
                CancelSite(nearbyCamp);
        }

        void OnDestroy()
        {
            Carcass.MeatDropped -= SpawnMeat;
            TorchLit = false;
            CleanUpStructures();
        }

        // ------------------------------------------------------------------ targeting
        void FindTarget(Vector3 origin, Vector3 forward)
        {
            nearbyPickup = null;
            nearbyLog = null;
            nearbyCamp = null;
            nearbyWater = false;
            float nearest = float.PositiveInfinity;
            foreach (var pickup in pickups)
                ConsiderPickup(pickup);
            foreach (var pickup in dropPickups)
                ConsiderPickup(pickup);

            void ConsiderPickup(ResourcePickup pickup)
            {
                if (pickup == null || !pickup.isActiveAndEnabled || !TryGetSupply(pickup.itemId, out var supply))
                    return;

                Vector3 delta = pickup.transform.position + Vector3.up * .25f - origin;
                float distance = delta.magnitude;
                if (distance >= nearest || distance > pickup.interactDistance ||
                    Vector3.Dot(delta, forward) < distance * .62f)
                    return;
                if (Obstructed(origin, delta, pickup.transform))
                    return;

                nearest = distance;
                nearbyPickup = pickup;
                Hint = Count(supply) >= StackLimit
                    ? $"{SupplyName(supply)} · mochila llena"
                    : $"[E] Recoger {pickup.displayName.ToLowerInvariant()}";
            }

            // Logs are long: aim anywhere along them.
            foreach (var log in worldLogs)
            {
                if (log == null)
                    continue;
                Vector3 axis = log.transform.right * (LogLength * .5f * log.transform.lossyScale.x);
                Vector3 point = ClosestOnSegment(log.transform.position - axis, log.transform.position + axis, origin, forward);
                Vector3 delta = point - origin;
                float distance = delta.magnitude;
                if (distance >= nearest || distance > 3.2f || Vector3.Dot(delta, forward) < distance * .8f)
                    continue;
                nearest = distance;
                nearbyPickup = null;
                nearbyLog = log;
                Hint = carriedLogs >= MaxCarriedLogs ? "Ya llevas dos troncos · G para soltar uno"
                    : $"[E] Cargar tronco ({carriedLogs + 1}/{MaxCarriedLogs})";
            }

            foreach (var piece in camp)
            {
                Vector3 point = piece.Bounds.ClosestPoint(origin);
                Vector3 delta = point - origin;
                float distance = delta.magnitude;
                Vector3 toCentre = piece.Bounds.center - origin;
                if (distance >= nearest || distance > 2.6f ||
                    distance > .6f && Vector3.Dot(toCentre.normalized, forward) < .35f)
                    continue;
                string hint = CampHint(piece);
                if (hint == null)
                    continue;
                nearest = distance;
                nearbyPickup = null;
                nearbyLog = null;
                nearbyCamp = piece;
                Hint = hint;
            }

            if (nearbyPickup == null && nearbyLog == null && nearbyCamp == null && LooksAtWater(origin, forward))
            {
                nearbyWater = true;
                Hint = Count(Supply.DirtyWater) >= StackLimit ? "Agua sucia · mochila llena" : "[E] Recoger agua sucia";
            }
        }

        bool Obstructed(Vector3 origin, Vector3 delta, Transform target) =>
            Physics.Linecast(origin, origin + delta, out var obstruction, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            !obstruction.transform.IsChildOf(target) && !obstruction.transform.IsChildOf(transform);

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 rayOrigin, Vector3 rayDirection)
        {
            // Closest point on segment ab to the ray (standard two-line solution, clamped).
            Vector3 u = b - a, v = rayDirection, w = a - rayOrigin;
            float uu = Vector3.Dot(u, u), uv = Vector3.Dot(u, v), vv = Vector3.Dot(v, v);
            float uw = Vector3.Dot(u, w), vw = Vector3.Dot(v, w);
            float denominator = uu * vv - uv * uv;
            float s = denominator > 1e-5f ? Mathf.Clamp01((uv * vw - vv * uw) / denominator) : 0f;
            return a + u * s;
        }

        bool LooksAtWater(Vector3 origin, Vector3 forward)
        {
            if (forward.y > -.05f)
                return false;
            float t = (SeaLevel - origin.y) / forward.y;
            if (t < 0 || t > 4f)
                return false;
            Vector3 surface = origin + forward * t;
            var terrain = Terrain.activeTerrain;
            return terrain != null && terrain.SampleHeight(surface) + terrain.transform.position.y < SeaLevel - .05f;
        }

        // ------------------------------------------------------------------ gathering
        void Gather(ResourcePickup pickup)
        {
            if (!pickup.isActiveAndEnabled || !TryGetSupply(pickup.itemId, out var supply))
                return;
            int gathered = Add(supply, pickup.amount);
            if (gathered == 0)
                return;
            pickup.gameObject.SetActive(false);
            if (dropPickups.Remove(pickup))
                Destroy(pickup.gameObject);
            else if (pickup.respawnSeconds > 0)
                StartCoroutine(Respawn(pickup));
            Notify($"+{gathered} {SupplyName(supply).ToLowerInvariant()}");
        }

        static System.Collections.IEnumerator Respawn(ResourcePickup pickup)
        {
            yield return new WaitForSeconds(pickup.respawnSeconds);
            if (pickup != null)
                pickup.gameObject.SetActive(true);
        }

        void FillDirtyWater()
        {
            if (Add(Supply.DirtyWater, 1) > 0)
                Notify("+1 agua sucia · hiérvela en el fogón antes de beber");
            else
                Notify("Mochila llena.");
        }

        int Add(Supply supply, int amount)
        {
            int slot = (int)supply;
            int added = Mathf.Clamp(amount, 0, StackLimit - inventory[slot]);
            inventory[slot] += added;
            for (int i = 0; i < added; i++)
            {
                if (supply == Supply.RawMeat) rawMeat.Add(Time.time + RawMeatLife);
                else if (supply == Supply.CookedMeat) cookedMeat.Add(Time.time + CookedMeatLife);
            }
            return added;
        }

        void Notify(string message)
        {
            Notice = message;
            NoticeUntil = Time.time + 4f;
        }

        internal static string SupplyName(Supply supply) => supply switch
        {
            Supply.Wood => "Madera",
            Supply.Stone => "Piedra",
            Supply.Fiber => "Fibra",
            Supply.Shoots => "Brotes",
            Supply.Food => "Raciones",
            Supply.Water => "Agua",
            Supply.RawMeat => "Carne cruda",
            Supply.CookedMeat => "Carne asada",
            Supply.DirtyWater => "Agua sucia",
            _ => "Carne podrida"
        };

        static bool TryGetSupply(string id, out Supply supply)
        {
            switch (id)
            {
                case "wood": supply = Supply.Wood; return true;
                case "stone": supply = Supply.Stone; return true;
                case "fiber": supply = Supply.Fiber; return true;
                case "shoots": supply = Supply.Shoots; return true;
                case "ration": supply = Supply.Food; return true;
                case "water": supply = Supply.Water; return true;
                case "meat_raw": supply = Supply.RawMeat; return true;
                default: supply = default; return false;
            }
        }
    }
}
