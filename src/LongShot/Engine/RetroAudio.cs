using System.Runtime.InteropServices;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace LongShot.Engine;

public static class RetroAudio
{
    private static IXAudio2 _xaudio2;
    private static IXAudio2MasteringVoice _masteringVoice;
    private static WaveFormat _format;

    public static void Init()
    {
        _xaudio2 = XAudio2.XAudio2Create();
        _masteringVoice = _xaudio2.CreateMasteringVoice();
        _format = new WaveFormat(44100, 16, 1);
    }

    /// <summary>
    /// Sharp, high-pitched "clack" (Square Wave + Fast Linear Decay)
    /// </summary>
    public static void PlayBallImpact(float impactPower)
    {
        float power = Math.Clamp(impactPower, 0f, 10f);
        float freq = 600f + (power * 40f);
        float duration = 0.05f + (power * 0.01f);
        float volume = Math.Min(power * 0.1f, 0.5f);

        PlayProceduralSound(duration, volume, (time, progress) =>
        {
            // C64-style Square Wave
            float wave = MathF.Sin(time * MathF.PI * 2f * freq) > 0 ? 1f : -1f;
            // Fast linear decay
            float envelope = 1.0f - progress;
            return wave * envelope;
        });
    }

    /// <summary>
    /// Dull, bouncy "thud" (Sine Wave + Pitch Drop + Smooth Decay)
    /// </summary>
    public static void PlayRailImpact(float impactPower)
    {
        float power = Math.Clamp(impactPower, 0f, 10f);
        float baseFreq = 150f + (power * 5f);
        float duration = 0.1f + (power * 0.02f);
        float volume = Math.Min(power * 0.15f, 0.6f);

        PlayProceduralSound(duration, volume, (time, progress) =>
        {
            // Pitch drops slightly as energy is absorbed by the cushion
            float currentFreq = baseFreq * (1.0f - (progress * 0.4f));
            // Sine wave for softer rubber feel
            float wave = MathF.Sin(time * MathF.PI * 2f * currentFreq);
            // Quadratic envelope for a softer attack/release curve
            float envelope = MathF.Pow(1.0f - progress, 1.5f);
            return wave * envelope;
        });
    }

    /// <summary>
    /// Woody "thwack" (Triangle Wave + Initial Noise Burst + Steep Decay)
    /// </summary>
    public static void PlayCueImpact(float impactPower)
    {
        float power = Math.Clamp(impactPower, 0f, 10f);
        float freq = 200f + (power * 10f);
        float duration = 0.08f + (power * 0.01f);
        float volume = Math.Min(power * 0.2f, 0.7f);

        PlayProceduralSound(duration, volume, (time, progress) =>
        {
            // Triangle wave for a hollow, wood-like resonance
            float period = 1f / freq;
            float wave = MathF.Abs((time % period) / period * 4f - 2f) - 1f;

            // Add a burst of white noise at the very start to simulate the chalk/tip striking
            float noise = progress < 0.1f ? (Random.Shared.NextSingle() * 2f - 1f) * 0.4f : 0f;

            // Steep exponential decay
            float envelope = MathF.Pow(1.0f - progress, 2f);
            return (wave + noise) * envelope;
        });
    }

    /// <summary>
    /// Core engine method: Handles unmanaged memory and XAudio2 buffering.
    /// </summary>
    /// <param name="duration">Duration of the sound in seconds.</param>
    /// <param name="volume">Volume multiplier (0.0 to 1.0).</param>
    /// <param name="waveGenerator">Function taking (timeInSeconds, normalizedProgress) returning amplitude (-1.0 to 1.0).</param>
    private static void PlayProceduralSound(float duration, float volume, Func<float, float, float> waveGenerator)
    {
        int sampleRate = _format.SampleRate;
        int totalSamples = (int)(sampleRate * duration);
        int byteCount = totalSamples * 2; // 16-bit audio = 2 bytes per sample

        IntPtr audioDataPtr = Marshal.AllocHGlobal(byteCount);

        unsafe
        {
            short* pSamples = (short*)audioDataPtr;

            for (int i = 0; i < totalSamples; i++)
            {
                float time = i / (float)sampleRate;
                float progress = (float)i / totalSamples;

                // Get waveform sample from the lambda
                float sample = waveGenerator(time, progress);

                // Apply master volume and clamp to prevent clipping distortion
                sample = Math.Clamp(sample * volume, -1f, 1f);

                pSamples[i] = (short)(sample * short.MaxValue);
            }
        }

        IXAudio2SourceVoice voice = _xaudio2.CreateSourceVoice(_format);

        var buffer = new AudioBuffer
        {
            AudioDataPointer = audioDataPtr,
            AudioBytes = (uint)byteCount,
            Flags = BufferFlags.EndOfStream
        };

        voice.BufferEnd += (context) =>
        {
            Marshal.FreeHGlobal(audioDataPtr);
            voice.DestroyVoice();
        };

        voice.SubmitSourceBuffer(buffer);
        voice.Start();
    }
}