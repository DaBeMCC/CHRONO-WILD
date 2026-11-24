using System.Collections;
using System.Collections.Generic;
using ChronoWild.Player;
using UnityEngine;

namespace ChronoWild.Survival
{
    /// <summary>Keeps a few dryosaurs grazing around the island and replaces the ones that are hunted.</summary>
    public sealed class DryosaurHerd : MonoBehaviour
    {
        public GameObject model;
        public Vector3[] spawnPoints = new Vector3[0];
        [Min(1)] public int population = 8;
        [Tooltip("Seconds before a hunted dryosaur is replaced, out of the player's sight.")]
        public float respawnSeconds = 150f;
        public float minSpawnDistance = 45f;

        readonly List<Dryosaur> living = new List<Dryosaur>();
        Transform player;

        void Start()
        {
            var controller = FindAnyObjectByType<DebugFirstPersonController>();
            player = controller ? controller.transform : null;
            if (model == null || spawnPoints.Length == 0)
                return;
            for (int i = 0; i < population; i++)
                Spawn(spawnPoints[i % spawnPoints.Length], false);
        }

        void Spawn(Vector3 point, bool awayFromPlayer)
        {
            if (awayFromPlayer && player)
            {
                // Pick the first spawn point out of easy reach; skip this round if none is.
                Vector3? far = null;
                foreach (var candidate in Shuffled())
                    if ((candidate - player.position).sqrMagnitude > minSpawnDistance * minSpawnDistance)
                    {
                        far = candidate;
                        break;
                    }
                if (far == null)
                {
                    StartCoroutine(Replace(30f));
                    return;
                }
                point = far.Value;
            }
            var offset = Random.insideUnitCircle * 4f;
            var go = Instantiate(model, point + new Vector3(offset.x, 0, offset.y), Quaternion.Euler(0, Random.Range(0, 360f), 0), transform);
            go.name = "Dryosaurus";
            var dino = go.AddComponent<Dryosaur>();
            dino.player = player;
            dino.Died += OnDied;
            living.Add(dino);
        }

        IEnumerable<Vector3> Shuffled()
        {
            var list = new List<Vector3>(spawnPoints);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        void OnDied(Dryosaur dino)
        {
            living.Remove(dino);
            StartCoroutine(Replace(respawnSeconds));
        }

        IEnumerator Replace(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (living.Count < population)
                Spawn(Vector3.zero, true);
        }
    }
}
