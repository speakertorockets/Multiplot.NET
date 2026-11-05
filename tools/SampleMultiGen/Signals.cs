namespace SampleMultiGen;

/// <summary>One synthetic channel. <see cref="Next"/> is called once per sample, in time order.</summary>
internal sealed record Signal(string Name, string Units, string Description, Func<double, float> Next);

internal static class Signals
{
    /// <summary>Builds the channel set. Random channels use their own seeded generators, so output is reproducible.</summary>
    public static Signal[] Create(int seed, double rate)
    {
        var impulseRng = new Random(seed);
        var noiseRng = new Random(seed + 1);
        double impulseProbability = 1.0 / (5 * rate); // one impulse every 5 s on average

        return
        [
            // 0.2 Hz sine, +/-10 V
            new("SINE", "V", "Sine wave 0.2 Hz, 10 V amplitude",
                t => (float)(10 * Math.Sin(2 * Math.PI * 0.2 * t))),

            // stair step: one level per minute, 0 -> 10 -> 0 (20 minute cycle)
            new("STAIR", "level", "Stair step, 1 level/min, 0-10-0",
                t =>
                {
                    int k = (int)(Math.Floor(t / 60) % 20);
                    return k <= 10 ? k : 20 - k;
                }),

            // single-sample spikes of random height and sign on a zero baseline
            new("IMPULSE", "g", "Random impulses, avg 1 per 5 s, 5-50 g",
                _ => impulseRng.NextDouble() < impulseProbability
                    ? (float)((impulseRng.Next(2) == 0 ? -1 : 1) * (5 + 45 * impulseRng.NextDouble()))
                    : 0f),

            // Gaussian white noise (Box-Muller), mean 0, sigma 1
            new("NOISE", "mV", "Gaussian white noise, mean 0, sigma 1",
                _ =>
                {
                    double u1 = 1.0 - noiseRng.NextDouble(), u2 = noiseRng.NextDouble();
                    return (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
                }),

            // continuously increasing: elapsed seconds since start
            new("RAMP", "s", "Continuously increasing, elapsed seconds",
                t => (float)t),

            // sawtooth: 10 s period, -5 to +5
            new("SAWTOOTH", "V", "Sawtooth 10 s period, -5 to +5 V",
                t => (float)(10 * (t / 10 - Math.Floor(t / 10)) - 5)),
        ];
    }
}
