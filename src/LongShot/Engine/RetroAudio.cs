using System;
using System.Runtime.InteropServices;
using Vortice.XAudio2;
using Vortice.Multimedia;

namespace LongShot.Engine;

public static class RetroAudio
{
    private static IXAudio2 _xaudio2;
    private static IXAudio2MasteringVoice _masteringVoice;
    private static WaveFormat _format;

    public static void Init()
    {
        // Initialize the DirectX Audio Engine
        _xaudio2 = XAudio2.XAudio2Create();
        _masteringVoice = _xaudio2.CreateMasteringVoice();

        // Standard CD-quality format: 44.1kHz, 16-bit, Mono
        _format = new WaveFormat(44100, 16, 1);
    }

    public static void PlayImpact(float impactPower)
    {
        float power = Math.Clamp(impactPower, 0f, 10f);

        // Dynamic pitch and duration based on physics
        float freq = 600f + (power * 40f);
        float duration = 0.05f + (power * 0.01f);
        float volume = Math.Min(power * 0.1f, 0.5f);

        int sampleRate = _format.SampleRate;
        int totalSamples = (int)(sampleRate * duration);
        int byteCount = totalSamples * 2; // 16-bit audio = 2 bytes per sample

        // Allocate unmanaged memory for the audio hardware to read
        IntPtr audioDataPtr = Marshal.AllocHGlobal(byteCount);

        unsafe
        {
            short* pSamples = (short*)audioDataPtr;

            for (int i = 0; i < totalSamples; i++)
            {
                float time = i / (float)sampleRate;

                // C64 Square Wave
                float wave = MathF.Sin(time * MathF.PI * 2f * freq) > 0 ? 1f : -1f;

                // ADSR Envelope: Fast decay
                float envelope = 1.0f - ((float)i / totalSamples);

                // Convert float (-1.0 to 1.0) to 16-bit integer
                pSamples[i] = (short)(wave * envelope * volume * short.MaxValue);
            }
        }

        // Create a temporary voice for this specific beep
        IXAudio2SourceVoice voice = _xaudio2.CreateSourceVoice(_format);

        // Tell XAudio2 where our raw memory lives
        var buffer = new AudioBuffer
        {
            AudioDataPointer = audioDataPtr,
            AudioBytes = (uint)byteCount,
            Flags = BufferFlags.EndOfStream // Tells the engine this is the whole sound
        };

        // When the sound finishes playing, free the memory and destroy the voice!
        voice.BufferEnd += (context) =>
        {
            Marshal.FreeHGlobal(audioDataPtr);
            voice.DestroyVoice();
        };

        voice.SubmitSourceBuffer(buffer);
        voice.Start();
    }
}