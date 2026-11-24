using System;
using UnityEngine;
using ChronoWild.Core;

namespace ChronoWild.Health
{
    public sealed class PlayerHealth : MonoBehaviour
    {
        public const int Maximum = 100;
        public const int AttackDamage = 15;
        public event Action<int> HealthChanged;
        public event Action Damaged;
        public event Action Died;
        public int Current { get; private set; } = Maximum;
        public bool IsDead => Current <= 0;
        public void Heal(int amount)
        {
            if (IsDead || amount <= 0 || Current == Maximum)
                return;
            Current += Mathf.Min(amount, Maximum - Current);
            HealthChanged?.Invoke(Current);
        }

        /// <summary>Sets health directly (loading a saved game). Never kills.</summary>
        public void Restore(int value)
        {
            Current = Mathf.Clamp(value, 1, Maximum);
            HealthChanged?.Invoke(Current);
        }

        public void TakeDamage(int amount)
        {
            if (IsDead || amount <= 0) return;
            Current = Mathf.Clamp(Current - amount, 0, Maximum);
            HealthChanged?.Invoke(Current); Damaged?.Invoke();
            if (IsDead) { Died?.Invoke(); GameStateController.Instance?.Die(); }
        }
    }
}
