using System.Collections.Generic;
using ChronoWild.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChronoWild.Survival
{
    // Blueprint construction: the book places a translucent ghost anywhere on firm ground; the
    // player then completes it by bringing materials (logs from the shoulder first). Each unit
    // added turns the next pieces of the model solid, bottom-up.
    public sealed partial class SurvivalRun
    {
        sealed class CampPiece
        {
            public Transform Root;
            public Recipe Recipe;
            public Structure Kind => Recipe.Kind;
            public readonly int[] Added = new int[4];          // logs, wood, stone, fiber
            public int Progress => Added[0] + Added[1] + Added[2] + Added[3];
            public bool Complete;
            public float Water;
            public int StoredLogs;
            public Bounds Bounds;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Material[]> Materials = new List<Material[]>();
            public readonly List<Collider> Colliders = new List<Collider>();
            public readonly List<Behaviour> Effects = new List<Behaviour>();
            public readonly List<ParticleSystem> Particles = new List<ParticleSystem>();
            public Transform Store;
            public GameObject WaterSurface;
        }

        const int HolderCapacity = 12;
        static readonly Color GhostValid = new Color(.55f, .86f, 1f, .38f);
        static readonly Color GhostInvalid = new Color(1f, .32f, .2f, .38f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Material ghostMaterial;
        Recipe? blueprint;
        GameObject ghost;
        float ghostYaw;
        MaterialPropertyBlock ghostBlock;
        Material flameMaterial;

        internal void BeginBuild(Recipe recipe)
        {
            if (states.State != Core.GameState.Backpack)
                return;
            if (carriedLogs > 0 && recipe.Logs == 0)
                Notify("Puedes colocar el plano con troncos al hombro.");
            blueprint = recipe;
            ghostYaw = transform.eulerAngles.y;
            DestroyGhost();
            states.Resume();
        }

        // ------------------------------------------------------------------ placement
        void UpdatePlacement(Vector3 origin, Vector3 forward)
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse?.rightButton.wasPressedThisFrame == true)
            {
                blueprint = null;
                DestroyGhost();
                return;
            }

            var recipe = blueprint.Value;
            if (mouse != null)
                ghostYaw += mouse.scroll.ReadValue().y * .12f;
            if (keyboard?.rKey.wasPressedThisFrame == true)
                ghostYaw += 45f;

            bool floor = Physics.Raycast(origin, forward, out var hit, 9f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) && hit.collider is TerrainCollider;
            var rotation = Quaternion.Euler(0, ghostYaw, 0);
            bool valid = floor && CanPlace(recipe.Kind, hit, rotation);

            if (ghost == null)
            {
                ghost = BuildStructure(recipe.Kind).gameObject;
                ghost.name = "Plano · " + recipe.Name;
                foreach (var c in ghost.GetComponentsInChildren<Collider>())
                    c.enabled = false;
                foreach (var b in ghost.GetComponentsInChildren<Light>())
                    b.enabled = false;
                foreach (var p in ghost.GetComponentsInChildren<ParticleSystem>())
                    p.gameObject.SetActive(false);
                foreach (var r in ghost.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = GhostArray(r.sharedMaterials.Length);
            }
            ghost.SetActive(floor);
            if (floor)
            {
                ghost.transform.SetPositionAndRotation(hit.point, rotation);
                ghostBlock ??= new MaterialPropertyBlock();
                ghostBlock.SetColor(BaseColorId, valid ? GhostValid : GhostInvalid);
                foreach (var r in ghost.GetComponentsInChildren<Renderer>())
                    r.SetPropertyBlock(ghostBlock);
            }

            Hint = valid
                ? $"{recipe.Name} · clic para colocar el plano · R o rueda para girar · clic derecho cancela"
                : "Apunta a terreno firme, llano y despejado · clic derecho cancela";
            if (valid && mouse?.leftButton.wasPressedThisFrame == true)
            {
                var piece = PlaceSite(recipe, hit.point, ghostYaw, null);
                blueprint = null;
                DestroyGhost();
                Notify(recipe.Total == 0 ? $"{recipe.Name} construido." :
                    $"Plano colocado · trae {Missing(piece)} y pulsa E sobre él");
            }
        }

        void HidePlacement()
        {
            if (ghost != null)
                ghost.SetActive(false);
        }

        void DestroyGhost()
        {
            if (ghost != null)
                Destroy(ghost);
            ghost = null;
        }

        Material[] GhostArray(int length)
        {
            var array = new Material[length];
            for (int i = 0; i < length; i++)
                array[i] = ghostMaterial != null ? ghostMaterial : woodMaterial;
            return array;
        }

        static Vector3 Footprint(Structure kind) => kind switch
        {
            Structure.Fire => new Vector3(.75f, .45f, .75f),
            Structure.Shelter => new Vector3(1.2f, .6f, 1.1f),
            Structure.Collector => new Vector3(.62f, .5f, .62f),
            Structure.LogWall => new Vector3(1.55f, .6f, .28f),
            Structure.LogHolder => new Vector3(1.25f, .5f, .66f),
            _ => new Vector3(1.75f, .8f, 1.85f),
        };

        bool CanPlace(Structure kind, RaycastHit hit, Quaternion rotation)
        {
            float slope = kind == Structure.Cabin ? 13f : 18f;
            if (hit.point.y < 1f || Vector3.Angle(hit.normal, Vector3.up) > slope)
                return false;
            var half = Footprint(kind);
            float reach = Mathf.Max(half.x, half.z);
            Vector3 flat = hit.point - transform.position;
            flat.y = 0;
            if (flat.magnitude < reach * .6f + .7f)
                return false;

            foreach (var piece in camp)
            {
                var other = Footprint(piece.Kind);
                float spacing = kind == Structure.LogWall && piece.Kind == Structure.LogWall
                    ? .9f : (reach + Mathf.Max(other.x, other.z)) * .75f;
                if ((piece.Root.position - hit.point).sqrMagnitude < spacing * spacing)
                    return false;
            }

            var centre = hit.point + Vector3.up * (half.y + .35f);
            foreach (var c in Physics.OverlapBox(centre, half, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!(c is TerrainCollider) && !c.transform.IsChildOf(transform))
                    return false;
            return true;
        }

        // ------------------------------------------------------------------ sites
        CampPiece PlaceSite(Recipe recipe, Vector3 point, float yaw, int[] added)
        {
            var root = BuildStructure(recipe.Kind);
            root.SetPositionAndRotation(point, Quaternion.Euler(0, yaw, 0));
            var piece = new CampPiece { Root = root, Recipe = recipe };
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                    continue;
                piece.Renderers.Add(r);
                piece.Materials.Add(r.sharedMaterials);
            }
            piece.Colliders.AddRange(root.GetComponentsInChildren<Collider>(true));
            piece.Effects.AddRange(root.GetComponentsInChildren<Light>(true));
            piece.Particles.AddRange(root.GetComponentsInChildren<ParticleSystem>(true));
            piece.Store = root.Find("Troncos guardados");
            piece.WaterSurface = root.Find("Agua")?.gameObject;
            piece.Bounds = new Bounds(point + Vector3.up * .5f, Vector3.one * .5f);
            foreach (var r in piece.Renderers)
                piece.Bounds.Encapsulate(r.bounds);
            camp.Add(piece);
            if (added != null)
                for (int i = 0; i < 4; i++)
                    piece.Added[i] = Mathf.Min(added[i], recipe.Need(i));
            Refresh(piece);
            return piece;
        }

        void Refresh(CampPiece piece)
        {
            int total = piece.Recipe.Total;
            bool complete = piece.Progress >= total;
            int solid = complete ? piece.Renderers.Count : Mathf.FloorToInt(piece.Renderers.Count * piece.Progress / (float)total);
            for (int i = 0; i < piece.Renderers.Count; i++)
            {
                var r = piece.Renderers[i];
                if (r == null)
                    continue;
                if (i < solid)
                {
                    r.sharedMaterials = piece.Materials[i];
                    r.SetPropertyBlock(null);
                }
                else
                {
                    r.sharedMaterials = GhostArray(piece.Materials[i].Length);
                    ghostBlock ??= new MaterialPropertyBlock();
                    ghostBlock.SetColor(BaseColorId, GhostValid);
                    r.SetPropertyBlock(ghostBlock);
                }
            }
            foreach (var c in piece.Colliders)
                if (c != null) c.enabled = complete;
            foreach (var e in piece.Effects)
                if (e != null) e.enabled = complete;
            foreach (var p in piece.Particles)
                if (p != null) p.gameObject.SetActive(complete);
            if (piece.WaterSurface != null)
                piece.WaterSurface.SetActive(complete && piece.Water >= 1);
            if (complete && !piece.Complete)
            {
                piece.Complete = true;
                if (piece.Kind == Structure.Collector)
                    StartCoroutine(CollectRain(piece));
            }
            UpdateStoredLogs(piece);
        }

        bool AddToSite(CampPiece piece)
        {
            for (int material = 0; material < 4; material++)
            {
                if (piece.Added[material] >= piece.Recipe.Need(material))
                    continue;
                if (material == 0)
                {
                    if (carriedLogs == 0)
                        continue;
                    carriedLogs--;
                }
                else
                {
                    var supply = material == 1 ? Supply.Wood : material == 2 ? Supply.Stone : Supply.Fiber;
                    if (inventory[(int)supply] == 0)
                        continue;
                    inventory[(int)supply]--;
                }
                piece.Added[material]++;
                Energy = Mathf.Max(0, Energy - .004f);
                Refresh(piece);
                Notify(piece.Complete ? $"{piece.Recipe.Name} terminado." : $"Falta: {Missing(piece)}");
                return true;
            }
            Notify($"Te falta: {Missing(piece)}");
            return false;
        }

        string Missing(CampPiece piece)
        {
            var parts = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                int need = piece.Recipe.Need(i) - piece.Added[i];
                if (need > 0)
                    parts.Add($"{need} {MaterialNames[i].ToLowerInvariant()}");
            }
            return parts.Count == 0 ? "nada" : string.Join(", ", parts);
        }

        void CancelSite(CampPiece piece)
        {
            Add(Supply.Wood, piece.Added[1]);
            Add(Supply.Stone, piece.Added[2]);
            Add(Supply.Fiber, piece.Added[3]);
            for (int i = 0; i < piece.Added[0]; i++)
                SpawnWorldLog(piece.Root.position + piece.Root.right * (i % 2 == 0 ? 1 : -1) * .5f + piece.Root.forward * (1.2f + i * .35f),
                    piece.Root.eulerAngles.y);
            camp.Remove(piece);
            Destroy(piece.Root.gameObject);
            Notify("Plano cancelado · materiales recuperados");
        }

        string CampHint(CampPiece piece)
        {
            if (!piece.Complete)
                return CanContribute(piece) ? $"[E] Añadir material · {piece.Recipe.Name} {piece.Progress}/{piece.Recipe.Total}     [C] Cancelar"
                    : $"{piece.Recipe.Name} · falta {Missing(piece)}     [C] Cancelar";
            switch (piece.Kind)
            {
                case Structure.Fire:
                    if (Count(Supply.RawMeat) > 0) return $"[E] Asar carne ({Count(Supply.RawMeat)})";
                    if (Count(Supply.DirtyWater) > 0) return $"[E] Hervir agua sucia ({Count(Supply.DirtyWater)})";
                    if (Count(Supply.Shoots) > 0) return $"[E] Asar un brote ({Count(Supply.Shoots)})";
                    return "Fogón · trae carne, brotes o agua sucia";
                case Structure.Shelter:
                case Structure.Cabin:
                    return IsNight ? "[E] Dormir hasta el amanecer y guardar" : "[E] Descansar 2 horas y guardar";
                case Structure.Collector:
                    return piece.Water >= 1 ? "[E] Recoger agua de lluvia" : "Recolector vacío · se llena con la lluvia";
                case Structure.LogHolder:
                    if (carriedLogs > 0 && piece.StoredLogs < HolderCapacity) return $"[E] Guardar tronco ({piece.StoredLogs}/{HolderCapacity})";
                    if (carriedLogs < MaxCarriedLogs && piece.StoredLogs > 0) return $"[E] Tomar tronco ({piece.StoredLogs}/{HolderCapacity})";
                    return $"Leñera · {piece.StoredLogs}/{HolderCapacity} troncos";
                default:
                    return null;
            }
        }

        bool CanContribute(CampPiece piece)
        {
            for (int i = 0; i < 4; i++)
            {
                if (piece.Added[i] >= piece.Recipe.Need(i))
                    continue;
                if (i == 0 ? carriedLogs > 0 : inventory[i == 1 ? 0 : i == 2 ? 1 : 2] > 0)
                    return true;
            }
            return false;
        }

        void UseCamp(CampPiece piece)
        {
            if (!piece.Complete)
            {
                AddToSite(piece);
                return;
            }
            switch (piece.Kind)
            {
                case Structure.Fire:
                    Cook();
                    break;
                case Structure.Shelter:
                case Structure.Cabin:
                    Sleep(piece.Kind == Structure.Cabin);
                    break;
                case Structure.Collector:
                    if (piece.Water >= 1 && Add(Supply.Water, 1) > 0)
                    {
                        piece.Water--;
                        Notify("+1 agua de lluvia");
                    }
                    else
                        Notify(piece.Water < 1 ? "Aún no hay agua en el recolector." : "Mochila llena.");
                    break;
                case Structure.LogHolder:
                    if (carriedLogs > 0 && piece.StoredLogs < HolderCapacity)
                    {
                        carriedLogs--;
                        piece.StoredLogs++;
                    }
                    else if (carriedLogs < MaxCarriedLogs && piece.StoredLogs > 0)
                    {
                        carriedLogs++;
                        piece.StoredLogs--;
                    }
                    UpdateStoredLogs(piece);
                    break;
            }
        }

        void UpdateStoredLogs(CampPiece piece)
        {
            if (piece.Store == null)
                return;
            for (int i = 0; i < piece.Store.childCount; i++)
                piece.Store.GetChild(i).gameObject.SetActive(piece.Complete && i < piece.StoredLogs);
        }

        // ------------------------------------------------------------------ models
        /// <summary>Builds a structure at the origin. Children are ordered bottom-up so sites fill naturally.</summary>
        Transform BuildStructure(Structure kind)
        {
            var root = new GameObject(kind.ToString()).transform;
            switch (kind)
            {
                case Structure.Fire:
                    if (firePitPrefab != null)
                        Model(firePitPrefab, root, Vector3.zero, Quaternion.identity, Vector3.one * .8f, true);
                    else
                        for (int i = 0; i < 7; i++)
                        {
                            float angle = i * 2 * Mathf.PI / 7;
                            Shape(root, PrimitiveType.Sphere, new Vector3(Mathf.Cos(angle) * .43f, .11f, Mathf.Sin(angle) * .43f),
                                new Vector3(.25f, .23f, .22f), Quaternion.identity, stoneMaterial);
                        }
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 120f;
                        Stake(root, Quaternion.Euler(0, a, 0) * new Vector3(.13f, .2f, 0), .62f,
                            Quaternion.Euler(0, a, 0) * Quaternion.Euler(0, 0, 62));
                    }
                    var glow = new GameObject("Brasas").AddComponent<Light>();
                    glow.transform.SetParent(root, false);
                    glow.transform.localPosition = Vector3.up * .55f;
                    glow.type = LightType.Point;
                    glow.range = 7;
                    glow.intensity = 2.2f;
                    glow.color = new Color(1, .48f, .14f);
                    glow.shadows = LightShadows.None;
                    AddFlame(root, Vector3.up * .22f, 1f);
                    break;
                case Structure.Shelter:
                    Pole(root, new Vector3(-.95f, .03f, .82f), new Vector3(-.95f, 1.62f, .82f), .09f);
                    Pole(root, new Vector3(.95f, .03f, .82f), new Vector3(.95f, 1.62f, .82f), .09f);
                    Pole(root, new Vector3(-1.03f, 1.58f, .82f), new Vector3(1.03f, 1.58f, .82f), .11f);
                    for (int i = 0; i < 7; i++)
                    {
                        float x = -.98f + i * .325f;
                        Pole(root, new Vector3(x, .1f, -.88f), new Vector3(x, 1.65f, .91f), .07f);
                    }
                    BuildWovenRoof(root);
                    Shape(root, PrimitiveType.Cube, new Vector3(0, .09f, .25f), new Vector3(1.5f, .13f, 1.05f),
                        Quaternion.identity, fiberMaterial);
                    break;
                case Structure.Collector:
                    for (int i = 0; i < 3; i++)
                        Pole(root, new Vector3(.56f, .02f, 0), new Vector3(.39f, .94f, 0), .08f, new Vector3(0, i * 120, 0));
                    BuildCollectorBowl(root);
                    var surface = Shape(root, PrimitiveType.Cylinder, new Vector3(0, .82f, 0),
                        new Vector3(.66f, .014f, .66f), Quaternion.identity, waterMaterial);
                    surface.name = "Agua";
                    Destroy(surface.GetComponent<Collider>());
                    break;
                case Structure.LogWall:
                    for (int x = -1; x <= 1; x += 2)
                        for (int z = -1; z <= 1; z += 2)
                            Stake(root, new Vector3(x * 1.35f, .62f, z * .2f), 1.35f, Quaternion.Euler(0, 0, 90));
                    for (int i = 0; i < 4; i++)
                        LogPiece(root, new Vector3(0, .15f + i * .27f, 0), false, 3.05f);
                    break;
                case Structure.LogHolder:
                    for (int x = -1; x <= 1; x += 2)
                        LogPiece(root, new Vector3(x * .9f, .1f, 0), true, 1.3f);
                    for (int x = -1; x <= 1; x += 2)
                        for (int z = -1; z <= 1; z += 2)
                            Stake(root, new Vector3(x * 1.15f, .55f, z * .55f), 1.1f, Quaternion.Euler(0, 0, 90));
                    var store = new GameObject("Troncos guardados").transform;
                    store.SetParent(root, false);
                    for (int i = 0; i < HolderCapacity; i++)
                    {
                        int row = i / 4, column = i % 4;
                        var log = LogPiece(store, new Vector3(0, .36f + row * .26f, -.42f + column * .28f + (row % 2) * .14f), false, 2.4f);
                        foreach (var c in log.GetComponentsInChildren<Collider>())
                            Destroy(c);
                    }
                    break;
                case Structure.Cabin:
                    for (int i = 0; i < 6; i++)
                    {
                        float y = .15f + i * .27f;
                        LogPiece(root, new Vector3(0, y, 1.5f), false, 3.3f);
                        LogPiece(root, new Vector3(-1.5f, y + .135f, 0), true, 3.3f);
                        LogPiece(root, new Vector3(1.5f, y + .135f, 0), true, 3.3f);
                    }
                    for (int x = -1; x <= 1; x += 2)
                        Stake(root, new Vector3(x * .55f, .78f, -1.5f), 1.6f, Quaternion.Euler(0, 0, 90));
                    // Front wall beside the door and the lintel above it.
                    for (int i = 0; i < 6; i++)
                        for (int x = -1; x <= 1; x += 2)
                            LogPiece(root, new Vector3(x * 1.05f, .15f + i * .27f, -1.5f), false, .95f);
                    LogPiece(root, new Vector3(0, 1.56f, -1.5f), false, 3.3f);
                    LogPiece(root, new Vector3(0, 1.83f, -1.5f), false, 3.3f);
                    for (int i = 0; i < 7; i++)
                        LogPiece(root, new Vector3(0, 2.02f + i * .07f, -1.75f + i * .58f), false, 3.6f);
                    break;
            }
            return root;
        }

        Transform LogPiece(Transform root, Vector3 position, bool alongZ, float length)
        {
            var log = CreateLogModel("Tronco", root).transform;
            log.localPosition = position;
            log.localRotation = alongZ ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
            log.localScale = new Vector3(length / LogLength, 1, 1);
            var capsule = log.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 0;
            capsule.radius = .14f;
            capsule.height = LogLength;
            return log;
        }

        void Stake(Transform root, Vector3 position, float length, Quaternion rotation)
        {
            if (stickPrefab == null)
            {
                Pole(root, position - rotation * Vector3.right * length * .5f, position + rotation * Vector3.right * length * .5f, .06f);
                return;
            }
            Model(stickPrefab, root, position, rotation, new Vector3(length, 1.2f, 1.2f), false);
        }

        static GameObject Model(GameObject prefab, Transform root, Vector3 position, Quaternion rotation, Vector3 scale, bool collide)
        {
            var go = Instantiate(prefab, root, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            foreach (var c in go.GetComponentsInChildren<Collider>())
                Destroy(c);
            if (collide)
            {
                var box = go.AddComponent<BoxCollider>();
                var filter = go.GetComponentInChildren<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    box.center = filter.sharedMesh.bounds.center;
                    box.size = filter.sharedMesh.bounds.size;
                }
            }
            return go;
        }

        static GameObject Shape(Transform root, PrimitiveType primitive, Vector3 point, Vector3 scale,
            Quaternion rotation, Material material)
        {
            var shape = GameObject.CreatePrimitive(primitive);
            shape.transform.SetParent(root, false);
            shape.transform.localPosition = point;
            shape.transform.localRotation = rotation;
            shape.transform.localScale = scale;
            if (material != null)
                shape.GetComponent<Renderer>().sharedMaterial = material;
            return shape;
        }

        void Pole(Transform root, Vector3 a, Vector3 b, float width, Vector3 rotation = default)
        {
            var twist = Quaternion.Euler(rotation);
            a = twist * a;
            b = twist * b;
            var direction = b - a;
            Shape(root, PrimitiveType.Cylinder, (a + b) * .5f, new Vector3(width, direction.magnitude * .5f, width),
                Quaternion.FromToRotation(Vector3.up, direction), woodMaterial);
        }

        ParticleSystem AddFlame(Transform root, Vector3 position, float size)
        {
            if (flameTemplate == null)
                return null;

            if (flameMaterial == null)
            {
                flameMaterial = new Material(flameTemplate) { name = "Brasas de campamento" };
                flameMaterial.SetFloat("_NightDim", 0);
                flameMaterial.SetFloat("_SunAmount", 1);
                flameMaterial.SetFloat("_SoftDistance", .15f);
            }

            var flame = new GameObject("Llama");
            flame.transform.SetParent(root, false);
            flame.transform.localPosition = position;
            var particles = flame.AddComponent<ParticleSystem>();
            flame.GetComponent<ParticleSystemRenderer>().sharedMaterial = flameMaterial;
            // Particle modules with start-only values must be configured before emission.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.28f * size, .72f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(.08f * size, .18f * size);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, .30f, .04f), new Color(1, .76f, .21f));
            main.maxParticles = 26;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 22;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = .14f * size;
            flame.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            particles.Play();
            return particles;
        }

        System.Collections.IEnumerator CollectRain(CampPiece piece)
        {
            var interval = new WaitForSeconds(1);
            while (piece.Root != null)
            {
                if (piece.WaterSurface != null)
                    piece.WaterSurface.SetActive(piece.Water >= 1);
                yield return interval;
                if (WeatherSystem.Instance != null)
                    piece.Water = Mathf.Min(3, piece.Water + WeatherSystem.Instance.Rain * .12f);
            }
        }

        void CleanUpStructures()
        {
            if (flameMaterial != null)
                Destroy(flameMaterial);
            foreach (var mesh in campMeshes)
                Destroy(mesh);
        }
    }
}
