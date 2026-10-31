// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Animation's editing helpers (wave 7, F16): copy_track, optimize, compress.

namespace Aver.Fabric;

public partial class Animation
{
    /// <summary>Copies a track (path, type, interpolation, update mode, flags and every key with its transition, handles, arguments and offsets) to the end of another animation.</summary>
    public void CopyTrack(int trackIdx, Animation toAnimation)
    {
        if (Handle == 0 || toAnimation == null || toAnimation.Handle == 0 || trackIdx < 0 || trackIdx >= GetTrackCount()) return;
        int dst = PanimNative.aver_panim_track_add(toAnimation.Handle, (int)TrackGetType(trackIdx), -1);
        if (dst < 0) return;
        int d = toAnimation.Handle;
        PanimNative.aver_panim_track_set_path(d, dst, PanimNative.Str(PanimNative.aver_panim_track_path(Handle, trackIdx)));
        PanimNative.aver_panim_track_set_interp(d, dst, PanimNative.aver_panim_track_interp(Handle, trackIdx));
        PanimNative.aver_panim_track_set_update(d, dst, PanimNative.aver_panim_track_update(Handle, trackIdx));
        int flags = PanimNative.aver_panim_track_flags(Handle, trackIdx);
        if (flags >= 0) PanimNative.aver_panim_track_set_flags(d, dst, flags & 1, (flags >> 1) & 1, (flags >> 2) & 1);
        var v = new double[5];
        var h = new double[4];
        var o = new double[2];
        int keys = PanimNative.aver_panim_key_count(Handle, trackIdx);
        for (int k = 0; k < keys; k++)
        {
            PanimNative.aver_panim_key_value(Handle, trackIdx, k, v);
            string text = PanimNative.Str(PanimNative.aver_panim_key_text(Handle, trackIdx, k));
            int at = PanimNative.aver_panim_key_insert(d, dst, PanimNative.aver_panim_key_time(Handle, trackIdx, k), v, text, PanimNative.aver_panim_key_transition(Handle, trackIdx, k));
            if (at < 0) continue;
            if (PanimNative.aver_panim_key_handles(Handle, trackIdx, k, h) != 0) PanimNative.aver_panim_key_set_handles(d, dst, at, h[0], h[1], h[2], h[3]);
            if (PanimNative.aver_panim_key_offsets(Handle, trackIdx, k, o) != 0) PanimNative.aver_panim_key_set_offsets(d, dst, at, o[0], o[1]);
            int args = PanimNative.aver_panim_key_arg_count(Handle, trackIdx, k);
            for (int a = 0; a < args; a++)
            {
                PanimNative.aver_panim_key_arg_get(Handle, trackIdx, k, a, v);
                PanimNative.aver_panim_key_arg_set(d, dst, at, a, v, PanimNative.Str(PanimNative.aver_panim_key_arg_text(Handle, trackIdx, k, a)));
            }
        }
        toAnimation.Touch();
    }

    /// <summary>Always false: tracks are stored plainly (compression only saves memory in Godot).</summary>
    public bool TrackIsCompressed(int trackIdx) => false;

    /// <summary>Godot packs the 3D tracks into pages to save memory and drops nothing visible; the values here are unchanged, so this does nothing.</summary>
    public void Compress(uint pageSize = 8192, uint fps = 120, float splitTolerance = 4.0f) { }

    /// <summary>Removes the keys of linear position, rotation and scale tracks that interpolation between their neighbours reproduces:
    /// within <paramref name="allowedVelocityErr"/> (metres for position, a ratio for scale) or <paramref name="allowedAngularErr"/> (radians for rotation).
    /// Godot's own pass also rounds the values to <paramref name="precision"/> digits; that rounding is not done here.</summary>
    public void Optimize(float allowedVelocityErr = 0.01f, float allowedAngularErr = 0.01f, int precision = 3)
    {
        if (Handle == 0) return;
        bool changed = false;
        for (int t = 0; t < GetTrackCount(); t++)
        {
            TrackType type = TrackGetType(t);
            if (type != TrackType.Position3D && type != TrackType.Rotation3D && type != TrackType.Scale3D) continue;
            if (PanimNative.aver_panim_track_interp(Handle, t) != (int)PanimInterp.Linear) continue;
            int n = PanimNative.aver_panim_key_count(Handle, t);
            if (n < 3) continue;
            var times = new double[n];
            var vals = new double[n][];
            for (int k = 0; k < n; k++)
            {
                times[k] = PanimNative.aver_panim_key_time(Handle, t, k);
                vals[k] = new double[5];
                PanimNative.aver_panim_key_value(Handle, t, k, vals[k]);
            }
            var drop = new List<int>();
            int last = 0;
            for (int k = 1; k < n - 1; k++)
            {
                double span = times[k + 1] - times[last];
                double u = span > 1e-12 ? (times[k] - times[last]) / span : 0;
                if (!Redundant(type, vals[last], vals[k], vals[k + 1], u, allowedVelocityErr, allowedAngularErr)) { last = k; continue; }
                drop.Add(k);
            }
            for (int i = drop.Count - 1; i >= 0; i--) PanimNative.aver_panim_key_remove(Handle, t, drop[i]);
            changed = changed || drop.Count > 0;
        }
        if (changed) Touch();
    }

    // Does key `mid` lie, within the tolerance, where interpolating from `a` to `b` at fraction `u` puts it? Position keys are in engine centimetres.
    private static bool Redundant(TrackType type, double[] a, double[] mid, double[] b, double u, float velTol, float angTol)
    {
        if (type == TrackType.Rotation3D)
        {
            double[] qa = { a[1], a[2], a[3], a[4] }, qb = { b[1], b[2], b[3], b[4] }, qm = { mid[1], mid[2], mid[3], mid[4] };
            double dot = qa[0] * qb[0] + qa[1] * qb[1] + qa[2] * qb[2] + qa[3] * qb[3];
            double sign = dot < 0 ? -1 : 1;
            var q = new double[4];
            double len = 0;
            for (int i = 0; i < 4; i++) { q[i] = qa[i] * (1 - u) + qb[i] * sign * u; len += q[i] * q[i]; }
            len = Math.Sqrt(len);
            if (len < 1e-12) return false;
            double d = 0;
            for (int i = 0; i < 4; i++) d += q[i] / len * qm[i];
            return 2.0 * Math.Acos(Math.Min(1.0, Math.Abs(d))) <= angTol;
        }
        double tol = type == TrackType.Position3D ? velTol * FabricSpace.CmPerMetre : velTol;
        double err = 0;
        for (int i = 1; i <= 3; i++)
        {
            double e = a[i] * (1 - u) + b[i] * u - mid[i];
            err += e * e;
        }
        return Math.Sqrt(err) <= tol;
    }
}
