using UnityEngine;

namespace ChronoWild.World
{
    /// <summary>Turns particle life up and down with the clock and the rain: pollen and insects by dry day, fireflies by dry night.</summary>
    public class WeatherGatedParticles : MonoBehaviour
    {
        public ParticleSystem[] dryDay = new ParticleSystem[0];
        public ParticleSystem[] dryNight = new ParticleSystem[0];

        float[] dayRates, nightRates;

        void Start()
        {
            dayRates = Rates(dryDay);
            nightRates = Rates(dryNight);
        }

        static float[] Rates(ParticleSystem[] systems)
        {
            var rates = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++)
                rates[i] = systems[i].emission.rateOverTimeMultiplier;
            return rates;
        }

        void Update()
        {
            var clock = DayNightCycle.Instance;
            var weather = WeatherSystem.Instance;
            float light = clock != null ? clock.Daylight : 1f;
            float dry = weather != null ? 1f - Mathf.Clamp01(weather.Rain * 2.5f) : 1f;
            Scale(dryDay, dayRates, light * dry);
            Scale(dryNight, nightRates, (1f - light) * dry);
        }

        static void Scale(ParticleSystem[] systems, float[] rates, float amount)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                var emission = systems[i].emission;
                emission.rateOverTimeMultiplier = rates[i] * amount;
            }
        }
    }
}
