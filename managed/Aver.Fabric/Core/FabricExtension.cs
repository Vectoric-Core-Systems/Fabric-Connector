// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// The managed entry of the extension (extension.json managed.entry): the engine calls Load once, before any project script loads.

using Aver.Framework.Hosting;

namespace Aver.Fabric;

/// <summary>Declares the actor classes that are not nodes. The manifest's actorBase declares every <see cref="Node"/> type; the game mode, its controller and
/// the pawn the view is published through derive from the framework's actor types instead.</summary>
public sealed class FabricExtension : IAverExtension
{
    public void Load(ExtensionContext context) => context.DeclareActors(IsGameClass);

    internal static bool IsGameClass(Type t) => t == typeof(FabricPlayerController) || t == typeof(FabricViewPawn) || t == typeof(FabricGameMode);
}
