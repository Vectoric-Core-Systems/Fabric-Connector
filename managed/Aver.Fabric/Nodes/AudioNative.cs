// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Calls into Aver.Audio.Abi (modules/audio.abi, audio_abi.h): the bus graph, the effects and the players AudioServer and the
// AudioStreamPlayer nodes drive.
// The engine hands these over as the service aver.audio (its docs/EXTENSIONS.md); scripts/seam-bindings.py writes each declaration's body
// (Generated/Seams.g.cs) and fails Fabric's build when one disagrees with the engine's scripts/abi-services.lock.

using System.Runtime.InteropServices;
using System.Text;

namespace Aver.Fabric;

/// <summary>The exports of Aver.Audio.Abi that Aver.Fabric uses. A host without the library makes <see cref="Available"/> false and the audio
/// classes keep their properties but play nothing.</summary>
internal static unsafe partial class AudioNative
{

    private static int s_state;   // 0 unknown, 1 loaded, 2 missing

    internal static bool Available
    {
        get
        {
            if (s_state == 0)
            {
                try { aver_audio_is_offline(); s_state = 1; }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { s_state = 2; }
            }
            return s_state == 1;
        }
    }

    /// <summary>Forgets a failed probe (a test that has just copied the library beside itself).</summary>
    internal static void Reprobe() { if (s_state == 2) s_state = 0; }

    internal static string Text(Func<byte[], int, int> read, int guess = 256)
    {
        byte[] buf = new byte[guess];
        int len = read(buf, buf.Length);
        if (len >= buf.Length) { buf = new byte[len + 1]; len = read(buf, buf.Length); }
        return Encoding.UTF8.GetString(buf, 0, Math.Min(len, buf.Length - 1));
    }

    // ---- device and offline mode ----
    internal static partial int aver_audio_ready();
    internal static partial int aver_audio_sample_rate();
    internal static partial int aver_audio_channels();
    internal static partial void aver_audio_shutdown();
    internal static partial void aver_audio_collect();
    internal static partial int aver_audio_offline_init(int sampleRate, int channels);
    internal static partial int aver_audio_offline_mix(float[] outInterleaved, int frames);
    internal static partial int aver_audio_is_offline();

    // ---- sounds and voices ----
    internal static partial int aver_audio_load([MarshalAs(UnmanagedType.LPUTF8Str)] string utf8Path);
    internal static partial int aver_audio_load_pcm(float[] samples, int frames, int channels, int sampleRate);
    internal static partial void aver_audio_unload(int sound);
    internal static partial void aver_audio_stop(int voice);
    internal static partial void aver_audio_stop_all();
    internal static partial int aver_audio_playing(int voice);
    internal static partial void aver_audio_set_voice_volume(int voice, float volume);
    internal static partial void aver_audio_set_voice_pitch(int voice, float pitch);
    internal static partial void aver_audio_set_voice_position(int voice, float x, float y, float z);
    internal static partial void aver_audio_set_listener(float px, float py, float pz, float fx, float fy, float fz, float rx, float ry, float rz);
    internal static partial void aver_audio_fade_voice(int voice, float targetGain, float seconds, int curve, int stopWhenDone);

    internal static partial int aver_audio_play_ex(int sound, float volume, float pitch, int looping, int bus, int graphBus, float startSeconds, int paused, float x, float y, float z, float[]? spatial18);
    internal static partial int aver_audio_stream_play_ex([MarshalAs(UnmanagedType.LPUTF8Str)] string utf8Path, float volume, float pitch, int looping, int bus, int graphBus, float startSeconds, int paused, float x, float y, float z, float[]? spatial18);
    internal static partial void aver_audio_set_voice_bus(int voice, int graphBus);
    internal static partial void aver_audio_set_voice_paused(int voice, int paused);
    internal static partial void aver_audio_set_voice_spatial(int voice, float[] spatial18);
    internal static partial double aver_audio_voice_position(int voice);
    internal static partial double aver_audio_voice_length(int voice);
    internal static partial int aver_audio_voice_underruns(int voice);
    internal static partial void aver_audio_set_listener_velocity(float vx, float vy, float vz);
    internal static partial void aver_audio_set_speed_scale(float scale);
    internal static partial float aver_audio_speed_scale();
    internal static partial double aver_audio_sound_length(int sound);
    internal static partial void aver_audio_sound_set_loop(int sound, int beginFrame, int endFrame);
    internal static partial int aver_audio_live_play(float volume, float pitch, int bus, int graphBus, float x, float y, float z, float[]? spatial18, int rate, int channels, int ringFrames);
    internal static partial int aver_audio_live_push(int voice, float[] interleaved, int frames);
    internal static partial int aver_audio_live_free(int voice);
    internal static partial void aver_audio_live_flush(int voice);

    // ---- the bus graph ----
    internal static partial void aver_audio_graph_set_active(int active);
    internal static partial int aver_audio_graph_active();
    internal static partial int aver_audio_bus_count();
    internal static partial int aver_audio_bus_add(int at);
    internal static partial int aver_audio_bus_remove(int index);
    internal static partial int aver_audio_bus_move(int from, int to);
    internal static partial void aver_audio_bus_set_count(int count);
    internal static partial int aver_audio_bus_find([MarshalAs(UnmanagedType.LPUTF8Str)] string utf8Name);
    internal static partial int aver_audio_bus_name(int index, byte[] textOut, int cap);
    internal static partial void aver_audio_bus_set_name(int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8Name);
    internal static partial void aver_audio_bus_set_send(int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8Name);
    internal static partial int aver_audio_bus_send_name(int index, byte[] textOut, int cap);
    internal static partial void aver_audio_bus_set_volume_db(int index, float db);
    internal static partial float aver_audio_bus_get_volume_db(int index);
    internal static partial void aver_audio_bus_set_flag(int index, int flag, int value);
    internal static partial int aver_audio_bus_get_flag(int index, int flag);
    internal static partial float aver_audio_bus_peak_db(int index, int channel);
    internal static partial int aver_audio_bus_load_layout([MarshalAs(UnmanagedType.LPUTF8Str)] string utf8Path);
    internal static partial int aver_audio_bus_apply_layout_text([MarshalAs(UnmanagedType.LPUTF8Str)] string utf8Text);
    internal static partial int aver_audio_bus_layout_text(byte[] textOut, int cap);

    // ---- effects ----
    internal static partial int aver_audio_effect_add(int bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string effectClass, int at);
    internal static partial int aver_audio_effect_remove(int bus, int index);
    internal static partial int aver_audio_effect_move(int bus, int from, int to);
    internal static partial int aver_audio_effect_count(int bus);
    internal static partial int aver_audio_effect_id(int bus, int index);
    internal static partial int aver_audio_effect_find(int id, out int bus, out int index);
    internal static partial void aver_audio_effect_set_enabled(int bus, int index, int enabled);
    internal static partial int aver_audio_effect_get_enabled(int bus, int index);
    internal static partial void aver_audio_effect_set_sidechain(int bus, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8BusName);
    internal static partial int aver_audio_effect_sidechain(int bus, int index, byte[] textOut, int cap);
    internal static partial int aver_audio_effect_set_param(int id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, float value);
    internal static partial float aver_audio_effect_get_param(int id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    internal static partial int aver_audio_effect_class(int id, byte[] textOut, int cap);
    internal static partial int aver_audio_effect_set_impulse(int id, int sound);
    internal static partial int aver_audio_effect_param_count(int id);
    internal static partial int aver_audio_effect_param_info(int id, int index, byte[] nameOut, int cap, out float def, out float lo, out float hi);
    internal static partial int aver_audio_capture_available(int id);
    internal static partial int aver_audio_capture_read(int id, float[] dst, int maxFrames);
    internal static partial int aver_audio_capture_capacity(int id);
    internal static partial int aver_audio_capture_discarded(int id);
    internal static partial void aver_audio_capture_clear(int id);
    internal static partial int aver_audio_spectrum(int id, float fromHz, float toHz, int mode, out float outLeft, out float outRight);
}

/// <summary>AVER_AUDIO_BUSFLAG_*.</summary>
internal enum AudioBusFlag { Mute = 0, Solo = 1, Bypass = 2 }
