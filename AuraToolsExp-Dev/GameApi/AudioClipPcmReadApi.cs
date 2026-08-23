using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Audio;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Compatibility boundary for Unity's native AudioClip sample provider. Streaming
/// clips reject AudioClip.GetData, while this provider consumes the same decoder's
/// interleaved PCM frames without recording the AudioListener or mixer output.
/// </summary>
internal static class AudioClipPcmReadApi
{
    private static readonly Type? ClipExtensionsType = typeof(AudioClip).Assembly.GetType(
        "UnityEngine.Experimental.Audio.AudioClipExtensionsInternal",
        false);
    private static readonly MethodInfo? CreateProviderMethod = ClipExtensionsType?.GetMethod(
        "Internal_CreateAudioClipSampleProvider",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo? RemoveProviderMethod = typeof(AudioSampleProvider).GetMethod(
        "InternalRemove",
        BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo? IsValidMethod = typeof(AudioSampleProvider).GetMethod(
        "InternalIsValid",
        BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo? FormatInfoMethod = typeof(AudioSampleProvider).GetMethod(
        "InternalGetFormatInfo",
        BindingFlags.Static | BindingFlags.NonPublic);

    internal static bool TryCreate(
        AudioClip clip,
        out AudioClipPcmReader? reader,
        out string failure)
    {
        reader = null;
        failure = "";
        if (clip == null)
        {
            failure = "audio clip is null";
            return false;
        }
        if (CreateProviderMethod == null
            || RemoveProviderMethod == null
            || IsValidMethod == null
            || FormatInfoMethod == null)
        {
            failure = "Unity AudioSampleProvider API is unavailable";
            return false;
        }

        var providerId = 0u;
        try
        {
            var created = CreateProviderMethod.Invoke(
                null,
                new object[]
                {
                    clip,
                    0UL,
                    (long)clip.samples,
                    false,
                    false,
                    false
                });
            providerId = created is uint value ? value : 0u;
            if (providerId == 0u || !IsValid(providerId))
            {
                failure = "Unity rejected the AudioClip sample provider";
                return false;
            }

            var formatArguments = new object[] { providerId, (ushort)0, 0u };
            FormatInfoMethod.Invoke(null, formatArguments);
            var channels = (ushort)formatArguments[1];
            var frequency = (uint)formatArguments[2];
            if (channels != clip.channels || frequency != clip.frequency)
            {
                Remove(providerId);
                failure = "sample provider format mismatch: channels=" + channels
                          + ", frequency=" + frequency;
                return false;
            }

            var native = AudioSampleProvider.consumeSampleFramesNativeFunction;
            reader = new AudioClipPcmReader(
                providerId,
                channels,
                frequency,
                native,
                IsValid,
                Remove);
            return true;
        }
        catch (Exception ex)
        {
            Remove(providerId);
            var cause = ex is TargetInvocationException { InnerException: not null }
                ? ex.InnerException
                : ex;
            failure = cause.Message;
            return false;
        }
    }

    private static bool IsValid(uint providerId)
    {
        try
        {
            return IsValidMethod?.Invoke(null, new object[] { providerId }) is true;
        }
        catch
        {
            return false;
        }
    }

    private static void Remove(uint providerId)
    {
        try
        {
            if (providerId != 0u)
            {
                RemoveProviderMethod?.Invoke(null, new object[] { providerId });
            }
        }
        catch
        {
        }
    }

    internal sealed class AudioClipPcmReader : IDisposable
    {
        private uint providerId;
        private readonly AudioSampleProvider.ConsumeSampleFramesNativeFunction consume;
        private readonly Func<uint, bool> isValid;
        private readonly Action<uint> remove;

        internal AudioClipPcmReader(
            uint providerId,
            ushort channels,
            uint frequency,
            AudioSampleProvider.ConsumeSampleFramesNativeFunction consume,
            Func<uint, bool> isValid,
            Action<uint> remove)
        {
            this.providerId = providerId;
            Channels = channels;
            Frequency = frequency;
            this.consume = consume;
            this.isValid = isValid;
            this.remove = remove;
        }

        internal int Channels { get; }

        internal uint Frequency { get; }

        internal bool TryRead(
            float[] target,
            int requestedFrames,
            out int consumedFrames,
            out string failure)
        {
            consumedFrames = 0;
            failure = "";
            if (providerId == 0u || !isValid(providerId))
            {
                failure = "AudioSampleProvider became invalid";
                return false;
            }
            if (target == null
                || requestedFrames <= 0
                || target.Length < checked(requestedFrames * Channels))
            {
                failure = "AudioSampleProvider target buffer is invalid";
                return false;
            }

            var handle = GCHandle.Alloc(target, GCHandleType.Pinned);
            try
            {
                consumedFrames = checked((int)consume(
                    providerId,
                    handle.AddrOfPinnedObject(),
                    checked((uint)requestedFrames)));
                if (consumedFrames < 0 || consumedFrames > requestedFrames)
                {
                    failure = "AudioSampleProvider returned an invalid frame count: "
                              + consumedFrames;
                    return false;
                }
                GC.KeepAlive(consume);
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                return false;
            }
            finally
            {
                handle.Free();
            }
        }

        public void Dispose()
        {
            var releasing = providerId;
            providerId = 0u;
            if (releasing != 0u)
            {
                remove(releasing);
            }
        }
    }
}
