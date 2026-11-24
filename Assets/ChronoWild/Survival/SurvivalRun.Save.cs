using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ChronoWild.Story;
using ChronoWild.World;
using UnityEngine;

namespace ChronoWild.Survival
{
    // Sleeping writes a JSON save; "Continuar" in the main menu loads it on the island.
    public sealed partial class SurvivalRun
    {
        [Serializable]
        sealed class SaveData
        {
            public int version = 1;
            public float[] position;
            public float yaw;
            public int health;
            public float hunger, hydration, stamina, energy;
            public int day;
            public float hour;
            public int[] inventory;
            public float[] rawMeat, cookedMeat;           // seconds left before spoiling
            public int carriedLogs;
            public int[] tools;
            public int equipped;
            public float torchFuel;
            public PieceData[] pieces;
            public float[] felledTrees;                   // x, y, z triples
            public float[] looseLogs;                     // x, y, z, yaw
            public int[] takenPickups;                    // non-respawning pickups already collected
            public bool[] parts;
        }

        [Serializable]
        sealed class PieceData
        {
            public int kind;
            public float x, y, z, yaw;
            public int[] added;
            public float water;
            public int stored;
        }

        /// <summary>Set by the main menu's "Continuar" before the island loads.</summary>
        public static bool LoadRequested { get; set; }
        static string SavePath => Path.Combine(Application.persistentDataPath, "chronowild_survival.json");
        public static bool HasSave => File.Exists(SavePath);

        void SaveGame()
        {
            var clock = DayNightCycle.Instance;
            var data = new SaveData
            {
                position = new[] { transform.position.x, transform.position.y, transform.position.z },
                yaw = transform.eulerAngles.y,
                health = health.Current,
                hunger = Hunger, hydration = Hydration, stamina = Stamina, energy = Energy,
                day = clock != null ? clock.day : 1,
                hour = clock != null ? clock.hour : 9f,
                inventory = (int[])inventory.Clone(),
                rawMeat = Remaining(rawMeat),
                cookedMeat = Remaining(cookedMeat),
                carriedLogs = carriedLogs,
                tools = ToInts(owned),
                equipped = (int)equipped,
                torchFuel = torchFuel,
                parts = MissionDirector.Instance != null ? MissionDirector.Instance.CollectedFlags() : null,
            };

            var pieces = new List<PieceData>();
            foreach (var piece in camp)
                pieces.Add(new PieceData
                {
                    kind = (int)piece.Kind,
                    x = piece.Root.position.x, y = piece.Root.position.y, z = piece.Root.position.z,
                    yaw = piece.Root.eulerAngles.y,
                    added = (int[])piece.Added.Clone(),
                    water = piece.Water,
                    stored = piece.StoredLogs,
                });
            data.pieces = pieces.ToArray();

            var trees = new List<float>();
            foreach (var p in felledTrees) { trees.Add(p.x); trees.Add(p.y); trees.Add(p.z); }
            data.felledTrees = trees.ToArray();

            var logs = new List<float>();
            foreach (var log in worldLogs)
                if (log != null)
                {
                    logs.Add(log.transform.position.x); logs.Add(log.transform.position.y);
                    logs.Add(log.transform.position.z); logs.Add(log.transform.eulerAngles.y);
                }
            data.looseLogs = logs.ToArray();

            var taken = new List<int>();
            for (int i = 0; i < pickups.Length; i++)
                if (pickups[i] != null && !pickups[i].gameObject.activeSelf && pickups[i].respawnSeconds <= 0)
                    taken.Add(i);
            data.takenPickups = taken.ToArray();

            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not save the game: {e.Message}");
                Notify("No se pudo guardar la partida.");
            }
        }

        IEnumerator LoadAfterStart()
        {
            // Let the clock, the intro and the mission director run their Start first.
            yield return null;
            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not load the saved game: {e.Message}");
                yield break;
            }
            if (data == null)
                yield break;

            var body = GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            transform.SetPositionAndRotation(new Vector3(data.position[0], data.position[1], data.position[2]), Quaternion.Euler(0, data.yaw, 0));
            if (body != null) body.enabled = true;

            health.Restore(data.health);
            Hunger = data.hunger;
            Hydration = data.hydration;
            Stamina = data.stamina;
            Energy = data.energy;
            var clock = DayNightCycle.Instance;
            if (clock != null)
            {
                clock.day = data.day;
                clock.SetHour(data.hour);
            }

            if (data.inventory != null)
                for (int i = 0; i < inventory.Length && i < data.inventory.Length; i++)
                    inventory[i] = data.inventory[i];
            rawMeat.Clear();
            cookedMeat.Clear();
            if (data.rawMeat != null) foreach (var t in data.rawMeat) rawMeat.Add(Time.time + t);
            if (data.cookedMeat != null) foreach (var t in data.cookedMeat) cookedMeat.Add(Time.time + t);
            carriedLogs = Mathf.Clamp(data.carriedLogs, 0, MaxCarriedLogs);
            owned.Clear();
            if (data.tools != null) foreach (var t in data.tools) owned.Add((Tool)t);
            torchFuel = data.torchFuel;
            Equip(owned.Contains((Tool)data.equipped) ? (Tool)data.equipped : Tool.None);

            if (data.pieces != null)
                foreach (var p in data.pieces)
                {
                    var recipe = Array.Find(Recipes, r => (int)r.Kind == p.kind);
                    var piece = PlaceSite(recipe, new Vector3(p.x, p.y, p.z), p.yaw, p.added);
                    piece.Water = p.water;
                    piece.StoredLogs = p.stored;
                    Refresh(piece);
                }

            if (data.felledTrees != null)
            {
                var trees = FindObjectsByType<FellableTree>(FindObjectsSortMode.None);
                for (int i = 0; i + 2 < data.felledTrees.Length; i += 3)
                {
                    var spot = new Vector3(data.felledTrees[i], data.felledTrees[i + 1], data.felledTrees[i + 2]);
                    felledTrees.Add(spot);
                    foreach (var tree in trees)
                        if (tree != null && (tree.transform.position - spot).sqrMagnitude < .5f)
                        {
                            float radius = tree.TryGetComponent<CapsuleCollider>(out var capsule)
                                ? Mathf.Clamp(capsule.radius * Mathf.Max(tree.transform.lossyScale.x, tree.transform.lossyScale.z), .28f, .68f) : .48f;
                            tree.MarkFelled();
                            CreateStump(spot, radius);
                        }
                }
            }

            if (data.looseLogs != null)
                for (int i = 0; i + 3 < data.looseLogs.Length; i += 4)
                    SpawnWorldLog(new Vector3(data.looseLogs[i], data.looseLogs[i + 1], data.looseLogs[i + 2]), data.looseLogs[i + 3]);

            if (data.takenPickups != null)
                foreach (var i in data.takenPickups)
                    if (i >= 0 && i < pickups.Length && pickups[i] != null)
                        pickups[i].gameObject.SetActive(false);

            if (MissionDirector.Instance != null)
                MissionDirector.Instance.RestoreProgress(data.parts);
            Notify("Partida cargada.");
        }

        static float[] Remaining(List<float> timers)
        {
            var left = new float[timers.Count];
            for (int i = 0; i < timers.Count; i++)
                left[i] = Mathf.Max(1f, timers[i] - Time.time);
            return left;
        }

        static int[] ToInts(HashSet<Tool> tools)
        {
            var list = new List<int>();
            foreach (var t in tools) list.Add((int)t);
            return list.ToArray();
        }
    }
}
