using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace LongShot.Engine;

public static class RetroAudio
{
    private static IXAudio2 _xaudio2;
    private static IXAudio2MasteringVoice _masteringVoice;
    private static WaveFormat _format;

    // --- 3D AUDIO LISTENER STATE ---
    public static Vector3 ListenerPosition = new Vector3(0, 1.5f, -1.5f);
    public static Vector3 ListenerForward = Vector3.UnitZ;

    public static void Init()
    {
        _xaudio2 = XAudio2.XAudio2Create();

        // Explicitly request 2 channels (Stereo) so our 3D panning works!
        _masteringVoice = _xaudio2.CreateMasteringVoice(2, 44100);
        _format = new WaveFormat(44100, 16, 1);
    }

    public static void UpdateListener(Vector3 position, Vector3 forward)
    {
        ListenerPosition = position;
        ListenerForward = Vector3.Normalize(forward);
    }

    public static void PlayPocketDrop(Vector3 position, float entrySpeed)
    {
        // The faster the ball hits the pocket, the higher pitched and faster the sound!
        float speedMultiplier = Math.Clamp(entrySpeed / 2.0f, 0.5f, 2.0f);

        float durationSeconds = 0.4f / speedMultiplier;
        float startFrequency = 300f * speedMultiplier;
        float endFrequency = 1200f * speedMultiplier;
        float volume = 0.6f;

        PlayProceduralSound(durationSeconds, volume, position, (time, progress) =>
        {
            // Sweep the frequency UP quickly
            float baseFreq = startFrequency + (endFrequency - startFrequency) * progress;

            // Modulate with a low-frequency oscillator (LFO) to make it warble (the Waka-Waka)
            float lfo = MathF.Sin(progress * MathF.PI * 20f * speedMultiplier) * 150f;
            float currentFreq = baseFreq + lfo;

            // Generate a Triangle wave for that authentic retro arcade chip-tune sound!
            float period = 1f / currentFreq;
            float wave = MathF.Abs((time % period) / period * 4f - 2f) - 1f;

            // Volume envelope (quick fade in, smooth fade out)
            float envelope = 1.0f;
            if (progress < 0.1f) envelope = progress * 10f;
            else if (progress > 0.8f) envelope = (1.0f - progress) * 5f;

            return wave * envelope;
        });
    }

    /// <summary>
    /// Sharp, high-pitched "clack" with random variation and spin friction.
    /// </summary>
    public static void PlayBallImpact(float impactPower, Vector3 worldPosition, float spin = 0f)
    {
        float power = Math.Clamp(impactPower, 0f, 10f);

        float pitchJitter = (Random.Shared.NextSingle() * 50f) - 25f; // +/- 25Hz variance
        float freq = 600f + (power * 40f) + pitchJitter;

        float duration = 0.05f + (power * 0.01f);
        float volume = Math.Min(power * 0.1f, 0.5f);

        PlayProceduralSound(duration, volume, worldPosition, (time, progress) =>
        {
            // Core clack (Square wave)
            float wave = MathF.Sin(time * MathF.PI * 2f * freq) > 0 ? 1f : -1f;

            // 2. SPIN EFFECT: Mix in a burst of white noise if the ball is spinning rapidly
            float frictionNoise = 0f;
            if (spin > 2f)
            {
                // The faster the spin, the louder the "grinding" friction noise mix
                float noiseVolume = Math.Min(spin * 0.015f, 0.4f);
                frictionNoise = (Random.Shared.NextSingle() * 2f - 1f) * noiseVolume;
            }

            float envelope = 1.0f - progress;
            return (wave + frictionNoise) * envelope;
        });
    }

    public static void PlayRailImpact(float impactPower, Vector3 worldPosition)
    {
        float power = Math.Clamp(impactPower, 0f, 10f);

        // Add a tiny bit of random variation to rails too!
        float pitchJitter = (Random.Shared.NextSingle() * 10f) - 5f;
        float baseFreq = 150f + (power * 5f) + pitchJitter;

        float duration = 0.1f + (power * 0.02f);
        float volume = Math.Min(power * 0.15f, 0.6f);

        PlayProceduralSound(duration, volume, worldPosition, (time, progress) =>
        {
            float currentFreq = baseFreq * (1.0f - (progress * 0.4f));
            float wave = MathF.Sin(time * MathF.PI * 2f * currentFreq);
            float envelope = MathF.Pow(1.0f - progress, 1.5f);
            return wave * envelope;
        });
    }

    public static void PlayCueImpact(float impactPower, Vector3 worldPosition)
    {
        float power = Math.Clamp(impactPower, 0f, 10f);
        float freq = 200f + (power * 10f);
        float duration = 0.08f + (power * 0.01f);
        float volume = Math.Min(power * 0.2f, 0.7f);

        PlayProceduralSound(duration, volume, worldPosition, (time, progress) =>
        {
            float period = 1f / freq;
            float wave = MathF.Abs((time % period) / period * 4f - 2f) - 1f;
            float noise = progress < 0.1f ? (Random.Shared.NextSingle() * 2f - 1f) * 0.4f : 0f;
            float envelope = MathF.Pow(1.0f - progress, 2f);
            return (wave + noise) * envelope;
        });
    }

    private static void PlayProceduralSound(float duration, float volume, Vector3 worldPosition, Func<float, float, float> waveGenerator)
    {
        int sampleRate = _format.SampleRate;
        int totalSamples = (int)(sampleRate * duration);
        int byteCount = totalSamples * 2;

        IntPtr audioDataPtr = Marshal.AllocHGlobal(byteCount);

        unsafe
        {
            short* pSamples = (short*)audioDataPtr;

            for (int i = 0; i < totalSamples; i++)
            {
                float time = i / (float)sampleRate;
                float progress = (float)i / totalSamples;

                float sample = waveGenerator(time, progress);
                sample = Math.Clamp(sample, -1f, 1f);

                pSamples[i] = (short)(sample * short.MaxValue);
            }
        }

        IXAudio2SourceVoice voice = _xaudio2.CreateSourceVoice(_format);

        // ==========================================
        // 3D SPATIALIZATION MATH
        // ==========================================
        Vector3 dirToSound = worldPosition - ListenerPosition;
        float distance = dirToSound.Length();

        if (distance > 0.001f)
            dirToSound /= distance;
        else
            dirToSound = ListenerForward;

        float distanceAttenuation = 1.0f / (1.0f + distance * 0.8f);

        Vector3 listenerRight = Vector3.Cross(Vector3.UnitY, ListenerForward);
        if (listenerRight.LengthSquared() < 0.001f) listenerRight = Vector3.UnitX;
        else listenerRight = Vector3.Normalize(listenerRight);

        float pan = Vector3.Dot(dirToSound, listenerRight);

        float panAngle = (pan + 1f) * MathF.PI / 4f;

        float leftVol = MathF.Cos(panAngle) * distanceAttenuation * volume;
        float rightVol = MathF.Sin(panAngle) * distanceAttenuation * volume;

        float[] outputMatrix = new float[] { leftVol, rightVol };
        voice.SetOutputMatrix(_masteringVoice, 1, 2, outputMatrix);
        // ==========================================

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