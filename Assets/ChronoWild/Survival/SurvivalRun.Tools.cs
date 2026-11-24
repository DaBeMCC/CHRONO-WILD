using System.Collections;
using System.Collections.Generic;
using ChronoWild.Prototype;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChronoWild.Survival
{
    // Hand-made tools and weapons: crafted in the backpack, selected with 1-4 (F holsters),
    // swung with the left mouse button. Axes fell conifers; every tool hurts creatures.
    public sealed partial class SurvivalRun
    {
        internal enum Tool { None, Axe, StoneAxe, Spear, Club, Torch }

        internal readonly struct ToolRecipe
        {
            public readonly Tool Tool, Requires;
            public readonly string Name, Description, Preview;
            public readonly int Wood, Stone, Fiber;

            public ToolRecipe(Tool tool, string name, string description, string preview, int wood, int stone, int fiber, Tool requires = Tool.None)
            {
                Tool = tool;
                Name = name;
                Description = description;
                Preview = preview;
                Wood = wood;
                Stone = stone;
                Fiber = fiber;
                Requires = requires;
            }
        }

        internal static readonly ToolRecipe[] ToolRecipes =
        {
            new ToolRecipe(Tool.Axe, "Hacha de campo", "Tala coníferas en 4 golpes. Tecla 1.", "axe", 2, 2, 1),
            new ToolRecipe(Tool.StoneAxe, "Hacha reforzada", "Mejora tu hacha: tala en 2 golpes y pega más fuerte. Tecla 1.", "stoneaxe", 1, 2, 2, Tool.Axe),
            new ToolRecipe(Tool.Spear, "Lanza", "Mucho alcance: la mejor para cazar dryosaurios. Tecla 2.", "spear", 2, 1, 2),
            new ToolRecipe(Tool.Club, "Garrote", "Golpe pesado que hace retroceder a los raptores. Tecla 3.", "club", 2, 2, 1),
            new ToolRecipe(Tool.Torch, "Antorcha", "Alumbra 4 minutos y los raptores no se acercan. Tecla 4.", "torch", 1, 0, 2),
        };

        const float SwingDuration = .66f;
        const float Contact = .44f;
        const float TorchDuration = 240f;

        [SerializeField] Material freshWoodMaterial;
        [SerializeField] GameObject stoneAxePrefab, spearPrefab, clubPrefab, torchPrefab;

        readonly HashSet<Tool> owned = new HashSet<Tool>();
        readonly List<Vector3> felledTrees = new List<Vector3>();
        Tool equipped, lastEquipped = Tool.Axe;
        Transform held;
        Tool heldKind;
        Light torchLight;
        float torchFuel;
        float swingAt = float.NegativeInfinity, equipBlend;
        bool strikePending;

        internal bool HasAxe => owned.Contains(Tool.Axe) || owned.Contains(Tool.StoneAxe);
        internal Tool Equipped => equipped;
        internal float TorchFuel => torchFuel;
        internal bool Owns(Tool tool) => owned.Contains(tool) || tool == Tool.Axe && owned.Contains(Tool.StoneAxe);

        static int TreePower(Tool t) => t == Tool.Axe ? 1 : t == Tool.StoneAxe ? 2 : 0;
        static int Damage(Tool t) => t switch { Tool.Axe => 2, Tool.StoneAxe => 3, Tool.Spear => 3, Tool.Club => 4, _ => 1 };
        static float Reach(Tool t) => t switch { Tool.Spear => 3.7f, Tool.Club => 2.6f, Tool.Torch => 2.4f, _ => 3.0f };
        static float SwingCost(Tool t) => t == Tool.Club ? .13f : t == Tool.Spear ? .08f : .10f;
        internal static string ToolName(Tool t) => t switch
        {
            Tool.Axe => "Hacha", Tool.StoneAxe => "Hacha reforzada", Tool.Spear => "Lanza",
            Tool.Club => "Garrote", Tool.Torch => "Antorcha", _ => ""
        };

        internal bool CanCraft(ToolRecipe r) =>
            !owned.Contains(r.Tool) && (r.Requires == Tool.None || owned.Contains(r.Requires)) &&
            Count(Supply.Wood) >= r.Wood && Count(Supply.Stone) >= r.Stone && Count(Supply.Fiber) >= r.Fiber;

        internal void Craft(ToolRecipe r)
        {
            if (states.State != Core.GameState.Backpack || !CanCraft(r))
                return;
            inventory[(int)Supply.Wood] -= r.Wood;
            inventory[(int)Supply.Stone] -= r.Stone;
            inventory[(int)Supply.Fiber] -= r.Fiber;
            if (r.Tool == Tool.StoneAxe)
                owned.Remove(Tool.Axe);
            owned.Add(r.Tool);
            if (r.Tool == Tool.Torch)
                torchFuel = TorchDuration;
            Equip(r.Tool);
            Notify($"{r.Name} lista · {ToolKeys()}");
        }

        void Equip(Tool tool)
        {
            if (tool == Tool.Axe && owned.Contains(Tool.StoneAxe))
                tool = Tool.StoneAxe;
            equipped = tool;
            if (tool != Tool.None)
                lastEquipped = tool;
        }

        internal string ToolKeys()
        {
            var keys = new List<string>();
            if (HasAxe) keys.Add("1 " + ToolName(owned.Contains(Tool.StoneAxe) ? Tool.StoneAxe : Tool.Axe));
            if (owned.Contains(Tool.Spear)) keys.Add("2 Lanza");
            if (owned.Contains(Tool.Club)) keys.Add("3 Garrote");
            if (owned.Contains(Tool.Torch)) keys.Add($"4 Antorcha {Mathf.CeilToInt(torchFuel)}s");
            if (keys.Count > 0) keys.Add("F guardar");
            return string.Join("   ", keys);
        }

        void UpdateTools(Transform eye, Vector3 origin, bool active)
        {
            var keyboard = Keyboard.current;
            if (active && keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) TrySelect(Tool.Axe);
                if (keyboard.digit2Key.wasPressedThisFrame) TrySelect(Tool.Spear);
                if (keyboard.digit3Key.wasPressedThisFrame) TrySelect(Tool.Club);
                if (keyboard.digit4Key.wasPressedThisFrame) TrySelect(Tool.Torch);
                if (keyboard.fKey.wasPressedThisFrame)
                    Equip(equipped != Tool.None ? Tool.None : Owns(lastEquipped) ? lastEquipped : FirstOwned());
            }

            bool ready = active && equipped != Tool.None && !blueprint.HasValue && carriedLogs == 0;
            bool show = ready && (thirdPerson == null || !thirdPerson.ThirdPerson);
            if (held != null && heldKind != equipped)
            {
                Destroy(held.gameObject);
                held = null;
                torchLight = null;
            }
            if (show && held == null && eye != null)
                CreateHeld(eye, equipped);
            if (held != null)
                AnimateHeld(show, active);
            TorchLit = ready && equipped == Tool.Torch;
            if (torchLight != null)
                torchLight.enabled = TorchLit;

            if (!ready)
                strikePending = false;
            if (!active || blueprint.HasValue || eye == null)
                return;

            float reach = Reach(equipped == Tool.None ? Tool.Axe : equipped);
            Component target = null;
            bool aimed = Physics.Raycast(origin, eye.forward, out var hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (aimed)
                target = (Component)hit.collider.GetComponentInParent<FellableTree>() ??
                         (Component)hit.collider.GetComponentInParent<Dryosaur>() ??
                         hit.collider.GetComponentInParent<DinoAI>();

            if (target is FellableTree aimedTree && !aimedTree.Falling && hit.distance < 3.45f)
                Hint = ready && TreePower(equipped) > 0
                    ? $"Clic · Talar árbol ({Mathf.CeilToInt(aimedTree.Remaining / (float)TreePower(equipped))} golpes)"
                    : HasAxe ? "1 · Saca el hacha para talar" : "P · Fabrica un hacha para talar";

            if (strikePending && Time.time - swingAt >= SwingDuration * Contact)
            {
                strikePending = false;
                Strike(target, hit, eye.forward, origin);
            }

            if (!ready || Mouse.current?.leftButton.wasPressedThisFrame != true || Time.time - swingAt < SwingDuration + .04f)
                return;
            if (Stamina < SwingCost(equipped) + .01f)
            {
                Notify("Necesitas recuperar resistencia.");
                return;
            }
            swingAt = Time.time;
            strikePending = true;
        }

        Tool FirstOwned()
        {
            foreach (var t in new[] { Tool.StoneAxe, Tool.Axe, Tool.Spear, Tool.Club, Tool.Torch })
                if (owned.Contains(t))
                    return t;
            return Tool.None;
        }

        void TrySelect(Tool tool)
        {
            if (!Owns(tool))
            {
                Notify($"No tienes {ToolName(tool).ToLowerInvariant()} · fabrícala en la mochila (P)");
                return;
            }
            Equip(equipped == tool || (tool == Tool.Axe && equipped == Tool.StoneAxe) ? Tool.None : tool);
        }

        void Strike(Component target, RaycastHit hit, Vector3 direction, Vector3 origin)
        {
            Stamina = Mathf.Max(0, Stamina - SwingCost(equipped));
            Energy = Mathf.Max(0, Energy - .006f);
            switch (target)
            {
                case FellableTree tree when TreePower(equipped) > 0:
                    if (tree.Strike(direction, CreateFelledWood, TreePower(equipped)))
                    {
                        Notify(tree.Falling ? "¡Cuidado, cae el árbol!" : "Golpe en el tronco");
                        if (!tree.Falling)
                            ShowCutMark(tree.transform, hit.point, hit.normal);
                        StartCoroutine(WoodChips(hit.point, hit.normal));
                    }
                    break;
                case Dryosaur dino:
                    dino.TakeHit(Damage(equipped), origin);
                    Notify(dino.IsDead ? "Cazaste un dryosaurio · recoge la carne" : "¡Le diste!");
                    break;
                case DinoAI raptor:
                    raptor.TakeHit(Damage(equipped), origin);
                    if (raptor.IsDead)
                        Notify("Abatiste al raptor.");
                    break;
            }
        }

        void CreateHeld(Transform eye, Tool tool)
        {
            heldKind = tool;
            var prefab = tool switch
            {
                Tool.StoneAxe => stoneAxePrefab, Tool.Spear => spearPrefab, Tool.Club => clubPrefab, Tool.Torch => torchPrefab, _ => null
            };
            if (prefab == null)
            {
                CreateAxe(eye);
            }
            else
            {
                held = new GameObject(ToolName(tool)).transform;
                held.SetParent(eye, false);
                var model = Instantiate(prefab, held, false).transform;
                foreach (var c in model.GetComponentsInChildren<Collider>())
                    Destroy(c);
                // Models: grip at the origin, pointing up. Shift so the hand holds it a little up the shaft.
                model.localPosition = tool == Tool.Spear ? Vector3.down * .55f : Vector3.down * .12f;
            }
            if (tool == Tool.Torch)
            {
                torchLight = new GameObject("Luz de antorcha").AddComponent<Light>();
                torchLight.transform.SetParent(held, false);
                torchLight.transform.localPosition = Vector3.up * .62f;
                torchLight.type = LightType.Point;
                torchLight.range = 11f;
                torchLight.intensity = 2.4f;
                torchLight.color = new Color(1, .58f, .26f);
                torchLight.shadows = LightShadows.None;
                AddFlame(held, Vector3.up * .58f, .7f);
            }
            foreach (var renderer in held.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void AnimateHeld(bool show, bool active)
        {
            equipBlend = active ? Mathf.MoveTowards(equipBlend, show ? 1f : 0f, Time.deltaTime * 5.4f) : 0;
            held.gameObject.SetActive(equipBlend > 0f);
            if (equipBlend <= 0f)
                return;

            float t = Mathf.Clamp01((Time.time - swingAt) / SwingDuration);
            bool spear = heldKind == Tool.Spear;
            var neutral = spear ? new Vector3(.27f, -.32f, .5f) : new Vector3(.31f, -.30f, .67f);
            var windup = spear ? new Vector3(.3f, -.27f, .32f) : new Vector3(.42f, -.12f, .57f);
            var impact = spear ? new Vector3(.12f, -.24f, 1.05f) : new Vector3(.025f, -.16f, .83f);
            var recoil = spear ? new Vector3(.2f, -.3f, .7f) : new Vector3(.13f, -.23f, .71f);
            var idleRotation = spear ? Quaternion.Euler(78, -6, 0) : Quaternion.Euler(-12, -23, -25);
            var backRotation = spear ? Quaternion.Euler(80, -4, 0) : Quaternion.Euler(-48, 22, -62);
            var hitRotation = spear ? Quaternion.Euler(86, -2, 0) : Quaternion.Euler(51, -57, 17);
            var recoilRotation = spear ? Quaternion.Euler(82, -5, 0) : Quaternion.Euler(30, -39, 5);
            if (heldKind == Tool.Torch)
            {
                idleRotation = backRotation = Quaternion.Euler(-8, -12, -14);
                neutral = windup = new Vector3(.3f, -.36f, .58f);
            }

            Vector3 position;
            Quaternion rotation;
            if (t < .28f)
            {
                float p = Mathf.SmoothStep(0, 1, t / .28f);
                position = Vector3.Lerp(neutral, windup, p);
                rotation = Quaternion.Slerp(idleRotation, backRotation, p);
            }
            else if (t < Contact)
            {
                float p = Mathf.InverseLerp(.28f, Contact, t);
                p *= p;
                position = Vector3.Lerp(windup, impact, p);
                rotation = Quaternion.Slerp(backRotation, hitRotation, p);
            }
            else if (t < .61f)
            {
                float p = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Contact, .61f, t));
                position = Vector3.Lerp(impact, recoil, p);
                rotation = Quaternion.Slerp(hitRotation, recoilRotation, p);
            }
            else
            {
                float p = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.61f, 1, t));
                position = Vector3.Lerp(recoil, neutral, p);
                rotation = Quaternion.Slerp(recoilRotation, idleRotation, p);
                position.x += Mathf.Sin(Time.time * 2.6f) * .008f * p;
                position.y += (Mathf.Sin(Time.time * 3f) * .008f +
                    Mathf.Sin(movement.DistanceTravelled * 4.4f) * (movement.IsRunning ? .018f : .008f)) * p;
            }

            float equippedBlend = Mathf.SmoothStep(0, 1, equipBlend);
            held.localPosition = position + Vector3.down * (.35f * (1 - equippedBlend));
            held.localRotation = Quaternion.Slerp(Quaternion.Euler(20, -20, -47), rotation, equippedBlend);
        }

        void UpdateTorch(float dt)
        {
            if (!owned.Contains(Tool.Torch))
                return;
            // Fuel burns only while the torch is out.
            if (equipped == Tool.Torch && carriedLogs == 0)
                torchFuel -= dt;
            if (torchFuel > 0)
                return;
            owned.Remove(Tool.Torch);
            if (equipped == Tool.Torch)
                Equip(Tool.None);
            Notify("La antorcha se apagó.");
        }

        void CreateAxe(Transform eye)
        {
            held = new GameObject("Hacha de campo").transform;
            held.SetParent(eye, false);
            var handle = Shape(held, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.047f, .29f, .044f),
                Quaternion.Euler(0, 0, -8), woodMaterial);
            handle.GetComponent<Collider>().enabled = false;
            for (int i = 0; i < 4; i++)
            {
                var wrap = Shape(held, PrimitiveType.Cylinder, new Vector3(.019f, .152f + i * .018f, 0),
                    new Vector3(.088f, .009f, .079f), Quaternion.Euler(i % 2 == 0 ? -16 : 15, 0, -8), fiberMaterial);
                wrap.GetComponent<Collider>().enabled = false;
            }

            Vector2[] blade = { new Vector2(-.18f,.135f), new Vector2(-.19f,.215f), new Vector2(-.153f,.292f),
                new Vector2(-.052f,.258f), new Vector2(.076f,.211f), new Vector2(.053f,.151f) };
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var indices = new List<int>();
            for (int side = -1; side <= 1; side += 2)
            {
                int offset = vertices.Count;
                foreach (var p in blade)
                {
                    vertices.Add(new Vector3(p.x, p.y, side * (p.x < -.14f ? .008f : .039f)));
                    uv.Add(p * 3.5f + Vector2.one * .5f);
                }
                for (int i = 1; i < blade.Length - 1; i++)
                {
                    indices.Add(offset);
                    indices.Add(offset + (side > 0 ? i + 1 : i));
                    indices.Add(offset + (side > 0 ? i : i + 1));
                }
            }
            for (int i = 0; i < blade.Length; i++)
            {
                int next = (i + 1) % blade.Length;
                indices.Add(i); indices.Add(next); indices.Add(i + blade.Length);
                indices.Add(next); indices.Add(next + blade.Length); indices.Add(i + blade.Length);
            }
            CampMesh(held, "Cabeza de piedra tallada", vertices, uv, indices, stoneMaterial);
        }

        void ShowCutMark(Transform tree, Vector3 point, Vector3 normal)
        {
            var mark = Shape(tree, PrimitiveType.Cube, Vector3.zero, Vector3.one, Quaternion.identity, freshWoodMaterial);
            mark.name = "Marca de tala";
            mark.transform.SetPositionAndRotation(point + normal * .006f, Quaternion.LookRotation(normal));
            Vector3 scale = tree.lossyScale;
            mark.transform.localScale = new Vector3(.18f / scale.x, .035f / scale.y, .015f / scale.z);
            mark.GetComponent<Collider>().enabled = false;
        }

        IEnumerator WoodChips(Vector3 point, Vector3 normal)
        {
            var cluster = new GameObject("Astillas");
            cluster.transform.position = point;
            for (int i = 0; i < 5; i++)
            {
                var chip = Shape(cluster.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(.021f,.065f,.025f),
                    Random.rotation, freshWoodMaterial);
                chip.GetComponent<Collider>().enabled = false;
                var body = chip.AddComponent<Rigidbody>();
                body.linearVelocity = normal * Random.Range(.45f, 1.5f) + Random.insideUnitSphere + Vector3.up * 1.1f;
                body.angularVelocity = Random.insideUnitSphere * 8;
            }
            yield return new WaitForSeconds(.64f);
            Destroy(cluster);
        }

        /// <summary>A felled tree leaves a stump and three logs to carry (two at a time).</summary>
        void CreateFelledWood(Vector3 ground, Vector3 heading, float radius)
        {
            felledTrees.Add(ground);
            CreateStump(ground, radius);
            Vector3 side = Vector3.Cross(Vector3.up, heading);
            float yaw = Quaternion.LookRotation(heading).eulerAngles.y - 90f;
            for (int i = 0; i < 3; i++)
                SpawnWorldLog(ground + heading * (2.4f + i * 3.1f) + side * ((i % 2 == 0 ? -1 : 1) * .45f), yaw + Random.Range(-8f, 8f));
        }

        void CreateStump(Vector3 ground, float radius)
        {
            if (stumpPrefab != null)
            {
                var stump = Instantiate(stumpPrefab, ground, Quaternion.Euler(0, Random.Range(0, 360f), 0));
                stump.name = "Tocón";
                stump.transform.localScale = new Vector3(radius * 2.6f, radius * 2.2f, radius * 2.6f);
                stump.AddComponent<CapsuleCollider>().radius = .35f;
            }
            else
            {
                var stump = new GameObject("Tocón").transform;
                stump.position = ground;
                Shape(stump, PrimitiveType.Cylinder, Vector3.up * .2f, new Vector3(radius * 2.15f, .2f, radius * 2.15f), Quaternion.identity, woodMaterial);
            }
        }
    }
}
