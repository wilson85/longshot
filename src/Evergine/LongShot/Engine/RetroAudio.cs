using System;
using System.IO;
using Evergine.Common.Audio;
using Evergine.Common.Media;
using Evergine.Components.Sound;
using Evergine.Framework;

namespace LongShot.Engine;

public class ProceduralAudioPlayer : Behavior
{
    [BindComponent]
    private SoundEmitter3D soundEmitter;

    [BindService]
    private AudioDevice audioDevice;

    protected override void Update(System.TimeSpan gameTime)
    {
    }

    public void PlayBallImpact(float power, float spin)
    {
        if (soundEmitter.PlayState != PlayState.Playing)
        {
            byte[] rawPcmData = RetroAudioGenerator.GenerateBallImpact(power, spin);

            using var stream = new MemoryStream(rawPcmData);

            var format = new WaveFormat(false, RetroAudioGenerator.SampleRate);

            AudioBuffer buffer = audioDevice.CreateAudioBuffer();
            buffer.Fill(stream, rawPcmData.Length, format);

            soundEmitter.Audio = buffer;
            soundEmitter.Play();
        }
    }
}


public static class RetroAudioGenerator
{
    // 44100Hz, 16-bit, Mono (Must be Mono for 3D spatialization to work properly in engines)
    public const int SampleRate = 44100;

    public static byte[] GeneratePocketDrop(float entrySpeed)
    {
        float speedMultiplier = Math.Clamp(entrySpeed / 2.0f, 0.5f, 2.0f);
        float durationSeconds = 0.4f / speedMultiplier;
        float startFrequency = 300f * speedMultiplier;
        float endFrequency = 1200f * speedMultiplier;

        return GenerateProceduralSound(durationSeconds, (time, progress) =>
        {
            float baseFreq = startFrequency + (endFrequency - startFrequency) * progress;
            float lfo = MathF.Sin(progress * MathF.PI * 20f * speedMultiplier) * 150f;
            float currentFreq = baseFreq + lfo;

            float period = 1f / currentFreq;
            float wave = MathF.Abs((time % period) / period * 4f - 2f) - 1f;

            float envelope = 1.0f;
            if (progress < 0.1f) envelope = progress * 10f;
            else if (progress > 0.8f) envelope = (1.0f - progress) * 5f;

            return wave * envelope;
        });
    }

    public static byte[] GenerateBallImpact(float impactPower, float spin = 0f)
    {
        float power = Math.Clamp(impactPower, 0f, 10f);
        float pitchJitter = (System.Random.Shared.NextSingle() * 50f) - 25f;
        float freq = 600f + (power * 40f) + pitchJitter;
        float duration = 0.05f + (power * 0.01f);

        // Note: Volume is scaled down slightly to avoid clipping when converted to short
        float volume = Math.Min(power * 0.1f, 0.5f);

        return GenerateProceduralSound(duration, (time, progress) =>
        {
            float wave = MathF.Sin(time * MathF.PI * 2f * freq) > 0 ? 1f : -1f;
            float frictionNoise = 0f;

            if (spin > 2f)
            {
                float noiseVolume = Math.Min(spin * 0.015f, 0.4f);
                frictionNoise = (System.Random.Shared.NextSingle() * 2f - 1f) * noiseVolume;
            }

            float envelope = 1.0f - progress;
            return (wave + frictionNoise) * envelope * volume;
        });
    }

    // Safe, managed memory generation
    private static byte[] GenerateProceduralSound(float duration, Func<float, float, float> waveGenerator)
    {
        int totalSamples = (int)(SampleRate * duration);

        // 2 bytes per sample (16-bit)
        byte[] audioData = new byte[totalSamples * 2];

        for (int i = 0; i < totalSamples; i++)
        {
            float time = i / (float)SampleRate;
            float progress = (float)i / totalSamples;

            float sample = waveGenerator(time, progress);
            sample = Math.Clamp(sample, -1f, 1f);

            short shortSample = (short)(sample * short.MaxValue);

            // Write the short into the byte array (Little Endian)
            audioData[i * 2] = (byte)(shortSample & 0xFF);
            audioData[i * 2 + 1] = (byte)((shortSample >> 8) & 0xFF);
        }

        return audioData;
    }
}