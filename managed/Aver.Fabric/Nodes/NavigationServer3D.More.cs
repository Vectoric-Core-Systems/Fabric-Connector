// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The rest of NavigationServer3D's agent getters and its debug switch.

namespace Aver.Fabric;

public static partial class NavigationServer3D
{
    private static bool s_debugEnabled;

    /// <summary>Whether the navigation debug drawing is on (Godot's set_debug_enabled). Stored: the engine draws no navigation overlay from a script.</summary>
    public static void SetDebugEnabled(bool enabled) => s_debugEnabled = enabled;

    public static bool GetDebugEnabled() => s_debugEnabled;

    public static float AgentGetRadius(Rid agent) => AgentInfo(agent)[(int)NavAgent.Radius] / FabricSpace.CmPerMetre;
    public static float AgentGetHeight(Rid agent) => AgentInfo(agent)[(int)NavAgent.Height] / FabricSpace.CmPerMetre;
    public static float AgentGetMaxSpeed(Rid agent) => AgentInfo(agent)[(int)NavAgent.MaxSpeed] / FabricSpace.CmPerMetre;
    public static float AgentGetNeighborDistance(Rid agent) => AgentInfo(agent)[(int)NavAgent.NeighborDist] / FabricSpace.CmPerMetre;
    public static int AgentGetMaxNeighbors(Rid agent) => (int)AgentInfo(agent)[(int)NavAgent.MaxNeighbors];
    public static float AgentGetTimeHorizonAgents(Rid agent) => AgentInfo(agent)[(int)NavAgent.HorizonAgents];
    public static float AgentGetTimeHorizonObstacles(Rid agent) => AgentInfo(agent)[(int)NavAgent.HorizonObstacles];
    public static uint AgentGetAvoidanceLayers(Rid agent) => (uint)AgentInfo(agent)[(int)NavAgent.Layers];
    public static uint AgentGetAvoidanceMask(Rid agent) => (uint)AgentInfo(agent)[(int)NavAgent.Mask];
    public static float AgentGetAvoidancePriority(Rid agent) => AgentInfo(agent)[(int)NavAgent.Priority];
}
