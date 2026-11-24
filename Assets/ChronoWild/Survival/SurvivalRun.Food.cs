using System.Collections.Generic;
using ChronoWild.World;
using UnityEngine;

namespace ChronoWild.Survival
{
    // Needs and food: hunger, thirst, stamina and energy; raw meat spoils, dirty water and raw
    // meat can make the player sick, the fire cooks and boils, and sleeping restores energy and saves.
    public sealed partial class SurvivalRun
    {
        internal readonly struct Consumable
        {
            public readonly Supply Supply;
            public readonly string Name, Description, Preview, Verb;

            public Consumable(Supply supply, string name, string description, string preview, string verb)
            {
                Supply = supply;
                Name = name;
                Description = description;
                Preview = preview;
                Verb = verb;
            }
        }

        internal static readonly Consumable[] Consumables =
        {
            new Consumable(Supply.CookedMeat, "Carne asada", "Sacia mucho y devuelve energía. Se estropea con el tiempo.", "meat_cooked", "Comer"),
            new Consumable(Supply.Food, "Ración", "Lata del laboratorio: sacia y da algo de energía. No caduca.", "food", "Comer"),
            new Consumable(Supply.RawMeat, "Carne cruda", "Ásala en el fogón. Cruda sacia poco y puede enfermarte. Se pudre pronto.", "meat_raw", "Comer"),
            new Consumable(Supply.Shoots, "Brotes", "Ásalos en el fogón para preparar una ración. Crudos apenas alimentan.", "fire", "Comer"),
            new Consumable(Supply.SpoiledMeat, "Carne podrida", "Mala idea: es muy probable que te enferme.", "meat_raw", "Comer"),
            new Consumable(Supply.Water, "Agua limpia", "Hervida o de lluvia: hidrata sin riesgo.", "water_dirty", "Beber"),
            new Consumable(Supply.DirtyWater, "Agua sucia", "Hiérvela en el fogón. Sin hervir puede enfermarte.", "water_dirty", "Beber"),
        };

        const float RawMeatLife = 420f, CookedMeatLife = 1500f;
        readonly List<float> rawMeat = new List<float>();
        readonly List<float> cookedMeat = new List<float>();
        internal float MaxStamina => Mathf.Lerp(.3f, 1f, Energy);

        internal bool IsNight
        {
            get
            {
                var clock = DayNightCycle.Instance;
                return clock != null && (clock.hour >= 19f || clock.hour < 5.5f);
            }
        }

        void UpdateNeeds(float dt, bool running)
        {
            Hunger = Mathf.Max(0, Hunger - dt / (running ? 720f : 1080f));
            Hydration = Mathf.Max(0, Hydration - dt / (running ? 375f : 675f));

            // Energy: slow drain, faster when running or hauling logs. It caps how much stamina comes back.
            float drain = 1f / 1500f + (running ? 1f / 320f : 0f) + carriedLogs * (1f / 420f);
            Energy = Mathf.Clamp01(Energy - drain * dt);
            float recovery = Mathf.Lerp(.12f, .28f, Mathf.Min(Hunger, Hydration));
            if (running)
                Stamina = Mathf.Max(0, Stamina - .16f * dt);
            else if (Stamina < MaxStamina)
                Stamina = Mathf.Min(MaxStamina, Stamina + recovery * dt);
            else
                Stamina = Mathf.MoveTowards(Stamina, MaxStamina, .05f * dt);
            if (Stamina <= .01f)
                exhausted = true;
            else if (Stamina >= .28f * MaxStamina)
                exhausted = false;
            movement.SprintAvailable = !exhausted && carriedLogs < MaxCarriedLogs;

            if (Hunger > 0 && Hydration > 0)
                damageIn = 0;
            else if ((damageIn += dt) >= 6f)
            {
                health.TakeDamage(4);
                damageIn = 0;
            }

            Spoil(rawMeat, Supply.RawMeat);
            Spoil(cookedMeat, Supply.CookedMeat);
            UpdateTorch(dt);
        }

        void Spoil(List<float> timers, Supply supply)
        {
            bool spoiled = false;
            while (timers.Count > 0 && timers[0] <= Time.time)
            {
                timers.RemoveAt(0);
                inventory[(int)supply]--;
                inventory[(int)Supply.SpoiledMeat] = Mathf.Min(StackLimit, inventory[(int)Supply.SpoiledMeat] + 1);
                spoiled = true;
            }
            if (spoiled)
                Notify(supply == Supply.RawMeat ? "La carne cruda se pudrió." : "La carne asada se estropeó.");
        }

        void TakeOldest(Supply supply)
        {
            inventory[(int)supply]--;
            if (supply == Supply.RawMeat && rawMeat.Count > 0) rawMeat.RemoveAt(0);
            else if (supply == Supply.CookedMeat && cookedMeat.Count > 0) cookedMeat.RemoveAt(0);
        }

        internal void Consume(Supply supply)
        {
            if (states.State != Core.GameState.Backpack || Count(supply) <= 0)
                return;
            bool drink = supply == Supply.Water || supply == Supply.DirtyWater;
            if (drink ? Hydration >= .99f : Hunger >= .99f)
            {
                Notify(drink ? "No tienes sed." : "No tienes hambre.");
                return;
            }

            float sickChance = 0f;
            switch (supply)
            {
                case Supply.CookedMeat: Hunger += .5f; Energy += .15f; break;
                case Supply.Food: Hunger += .46f; Energy += .08f; break;
                case Supply.RawMeat: Hunger += .2f; sickChance = .45f; break;
                case Supply.Shoots: Hunger += .07f; break;
                case Supply.SpoiledMeat: Hunger += .12f; sickChance = .85f; break;
                case Supply.Water: Hydration += .52f; break;
                case Supply.DirtyWater: Hydration += .4f; sickChance = .3f; break;
            }
            Hunger = Mathf.Clamp01(Hunger);
            Hydration = Mathf.Clamp01(Hydration);
            Energy = Mathf.Clamp01(Energy);
            TakeOldest(supply);
            if (Random.value < sickChance)
                Sicken();
            else
                Notify(drink ? "Bebiste." : "Comiste.");
        }

        void Sicken()
        {
            health.TakeDamage(10);
            Energy = Mathf.Max(0, Energy - .2f);
            Hydration = Mathf.Max(0, Hydration - .1f);
            Notify("Te sentó mal: pierdes salud, energía y agua.");
        }

        void Cook()
        {
            if (Count(Supply.RawMeat) > 0)
            {
                if (Count(Supply.CookedMeat) >= StackLimit) { Notify("No te cabe más carne asada."); return; }
                TakeOldest(Supply.RawMeat);
                Add(Supply.CookedMeat, 1);
                Notify("Carne asada · cómela desde la mochila (P)");
            }
            else if (Count(Supply.DirtyWater) > 0)
            {
                if (Count(Supply.Water) >= StackLimit) { Notify("No te cabe más agua limpia."); return; }
                inventory[(int)Supply.DirtyWater]--;
                Add(Supply.Water, 1);
                Notify("Agua hervida: ya es segura.");
            }
            else if (Count(Supply.Shoots) > 0)
            {
                if (Count(Supply.Food) >= StackLimit) { Notify("No te cabe otra ración."); return; }
                inventory[(int)Supply.Shoots]--;
                Add(Supply.Food, 1);
                Notify("Ración preparada.");
            }
            else
                Notify("Trae carne, brotes o agua sucia.");
        }

        /// <summary>Sleeping passes the night (or naps two hours by day), restores energy and saves the game.</summary>
        void Sleep(bool cabin)
        {
            if (Hunger <= RestHunger || Hydration <= RestThirst)
            {
                Notify("Come y bebe antes de dormir.");
                return;
            }
            bool night = IsNight;
            if (!night && Energy > .8f)
            {
                Notify("No tienes sueño. Vuelve de noche o cuando estés cansado.");
                return;
            }
            if (hasRested && Time.time - lastRest < 40f)
            {
                Notify("Acabas de descansar.");
                return;
            }

            hasRested = true;
            lastRest = Time.time;
            float hours = 2f;
            var clock = DayNightCycle.Instance;
            if (clock != null && night)
                hours = clock.hour >= 19f ? 24f - clock.hour + 6.5f : 6.5f - clock.hour;
            float ratio = Mathf.Clamp01(hours / 9f);
            Hunger = Mathf.Max(.02f, Hunger - RestHunger * ratio * 1.4f);
            Hydration = Mathf.Max(.02f, Hydration - RestThirst * ratio * 1.4f);
            Energy = night ? (cabin ? 1f : .85f) : Mathf.Min(1f, Energy + (cabin ? .45f : .35f));
            Stamina = MaxStamina;
            health.Heal(night ? (cabin ? 35 : 25) : 10);
            if (clock != null)
            {
                float destination = clock.hour + hours;
                clock.day += Mathf.FloorToInt(destination / 24f);
                clock.SetHour(destination);
            }
            SaveGame();
            Notify(night ? "Dormiste hasta el amanecer · partida guardada" : "Descansaste 2 horas · partida guardada");
        }
    }
}
