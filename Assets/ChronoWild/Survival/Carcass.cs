using System;
using UnityEngine;

namespace ChronoWild.Survival
{
    /// <summary>
    /// Creatures that die report where their meat lies; the survival loop turns it into pickups.
    /// Keeps the creature scripts independent of the inventory.
    /// </summary>
    public static class Carcass
    {
        public static event Action<Vector3, int> MeatDropped;

        public static void Drop(Vector3 position, int portions)
        {
            if (portions > 0)
                MeatDropped?.Invoke(position, portions);
        }
    }
}
