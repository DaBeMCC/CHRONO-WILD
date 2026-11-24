using System.Collections.Generic;
using ChronoWild.World;
using UnityEngine;

namespace ChronoWild.Survival
{
    // Logs are never stored in the backpack: they are carried on the shoulder, two at most,
    // which slows the player down and tires them out.
    public sealed partial class SurvivalRun
    {
        internal const int MaxCarriedLogs = 2;
        const float LogLength = 3.05f;

        [Header("Models")]
        [SerializeField] GameObject logPrefab;
        [SerializeField] GameObject stickPrefab;
        [SerializeField] GameObject stumpPrefab;
        [SerializeField] GameObject firePitPrefab;
        [SerializeField] GameObject bucketPrefab;
        [SerializeField] GameObject meatRawPrefab;

        readonly List<ResourcePickup> worldLogs = new List<ResourcePickup>();
        readonly Transform[] shoulderLogs = new Transform[MaxCarriedLogs];
        int carriedLogs;
        internal int CarriedLogs => carriedLogs;

        void UpdateCarry(bool active)
        {
            float speed = carriedLogs == 0 ? 1f : carriedLogs == 1 ? .86f : .7f;
            if (Energy < .12f)
                speed *= .8f;
            movement.SpeedMultiplier = speed;
            movement.JumpAvailable = carriedLogs < MaxCarriedLogs;

            bool third = thirdPerson != null && thirdPerson.isActiveAndEnabled && thirdPerson.ThirdPerson;
            for (int i = 0; i < MaxCarriedLogs; i++)
            {
                bool show = active && i < carriedLogs;
                if (show && shoulderLogs[i] == null)
                    shoulderLogs[i] = CreateLogModel("Tronco al hombro", null).transform;
                if (shoulderLogs[i] == null)
                    continue;
                shoulderLogs[i].gameObject.SetActive(show);
                if (!show)
                    continue;
                // First person: on the right shoulder pointing forward; third person: on the avatar.
                var parent = third ? transform : movement.cameraPivot;
                if (shoulderLogs[i].parent != parent)
                    shoulderLogs[i].SetParent(parent, false);
                shoulderLogs[i].localPosition = third
                    ? new Vector3(.24f - i * .2f, 1.62f + i * .1f, .1f)
                    : new Vector3(.38f - i * .24f, -.27f + i * .11f, .55f);
                shoulderLogs[i].localRotation = Quaternion.Euler(0, 90, i * 2f);
            }
        }

        void PickUpLog(ResourcePickup log)
        {
            if (carriedLogs >= MaxCarriedLogs)
            {
                Notify("Ya llevas dos troncos.");
                return;
            }
            carriedLogs++;
            worldLogs.Remove(log);
            Destroy(log.gameObject);
            Notify(carriedLogs == 1 ? "Tronco al hombro · G para soltarlo" : "Dos troncos: vas más lento y te cansas antes.");
        }

        void DropLog()
        {
            if (carriedLogs == 0)
                return;
            carriedLogs--;
            Vector3 spot = transform.position + transform.forward * 1.3f;
            SpawnWorldLog(spot, transform.eulerAngles.y + 90f);
        }

        /// <summary>A log lying on the ground that can be shouldered with E.</summary>
        ResourcePickup SpawnWorldLog(Vector3 position, float yaw, float scale = 1f)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
                position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            var go = CreateLogModel("Tronco", null);
            go.transform.SetPositionAndRotation(position + Vector3.up * .14f, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = new Vector3(scale, 1, 1);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 0;
            capsule.radius = .14f;
            capsule.height = LogLength;
            var pickup = go.AddComponent<ResourcePickup>();
            pickup.itemId = "log";
            pickup.displayName = "Tronco";
            pickup.amount = 1;
            pickup.interactDistance = 3.2f;
            worldLogs.Add(pickup);
            return pickup;
        }

        GameObject CreateLogModel(string name, Transform parent)
        {
            GameObject go;
            if (logPrefab != null)
            {
                go = Instantiate(logPrefab, parent, false);
                foreach (var c in go.GetComponentsInChildren<Collider>())
                    Destroy(c);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.localScale = new Vector3(.28f, LogLength * .5f, .28f);
                go.GetComponent<Renderer>().sharedMaterial = woodMaterial;
                var root = new GameObject();
                root.transform.SetParent(parent, false);
                go.transform.SetParent(root.transform, false);
                go.transform.localRotation = Quaternion.Euler(0, 0, 90);
                go = root;
            }
            go.name = name;
            return go;
        }

        void SpawnMeat(Vector3 position, int portions)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
                position.y = Mathf.Max(position.y, terrain.SampleHeight(position) + terrain.transform.position.y);
            GameObject go;
            if (meatRawPrefab != null)
            {
                go = Instantiate(meatRawPrefab, position + Vector3.up * .08f, Quaternion.Euler(0, Random.Range(0, 360f), 0));
                go.transform.localScale = Vector3.one * 1.4f;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.position = position + Vector3.up * .1f;
                go.transform.localScale = new Vector3(.3f, .15f, .2f);
            }
            go.name = "Carne cruda";
            var pickup = go.AddComponent<ResourcePickup>();
            pickup.itemId = "meat_raw";
            pickup.displayName = "Carne cruda";
            pickup.amount = portions;
            pickup.interactDistance = 3f;
            dropPickups.Add(pickup);
        }
    }
}
