using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ArchimedesScrew;

internal sealed class RealisticWaterCompatBridge : IDisposable
{
    private const string RealisticWaterModId = "realisticwater";
    private const string RealisticWaterFreshPath = "realisticwater-still-6-20";
    private const int OutletSustainTtlMs = 10000;
    private const int ShutdownDrainTtlMs = OutletSustainTtlMs;
    private const int ShutdownDrainCascadeRadius = 8;
    private const int RealisticWaterSustainedLevel = 6;
    private const int RealisticWaterSustainedSublevel = 20;

    private static readonly Dictionary<BlockPos, SustainedOutlet> sustainedOutletsByPos = new();
    private static readonly Dictionary<BlockPos, ShutdownDrainOrigin> shutdownDrainOriginsByPos = new();

    private readonly ICoreServerAPI api;
    private readonly Harmony harmony;
    private bool isPatched;

    public RealisticWaterCompatBridge(ICoreServerAPI api)
    {
        this.api = api;
        harmony = new Harmony($"{ArchimedesScrewModSystem.ModId}.compat.realisticwater");
        IsActive = api.ModLoader.IsModEnabled(RealisticWaterModId);
        RefreshPatchState();
        api.Logger.Notification(
            IsActive
                ? "{0} [compat/realisticwater] Compat active; outlet sustain patch enabled"
                : "{0} [compat/realisticwater] Mod not installed; compat inactive",
            ArchimedesScrewModSystem.LogPrefix);
    }

    public bool IsActive { get; }

    public void Dispose()
    {
        Unpatch();
        sustainedOutletsByPos.Clear();
        shutdownDrainOriginsByPos.Clear();
    }

    public bool TryResolveOutletBlock(string familyId, out Block outletBlock)
    {
        outletBlock = null!;
        if (!IsActive)
        {
            return false;
        }

        if (string.Equals(familyId, ArchimedesWaterFamilies.Fresh.Id, StringComparison.Ordinal) &&
            (TryGetBlock(new AssetLocation(RealisticWaterModId, RealisticWaterFreshPath), out outletBlock) ||
             TryGetBlock(new AssetLocation("game", RealisticWaterFreshPath), out outletBlock)))
        {
            return true;
        }

        ArchimedesWaterFamily family = ArchimedesWaterFamilies.GetById(familyId);
        return TryGetBlock(new AssetLocation("game", $"{family.VanillaCode}-still-6"), out outletBlock);
    }

    public bool TryResolveIntakeFamily(Block block, out string familyId)
    {
        if (IsActive &&
            block.IsLiquid() &&
            block.Code?.Path.StartsWith("realisticwater-", StringComparison.Ordinal) == true)
        {
            familyId = ArchimedesWaterFamilies.Fresh.Id;
            return true;
        }

        familyId = string.Empty;
        return false;
    }

    public void RefreshSustainedOutlet(BlockPos pos, string familyId, Block outletBlock)
    {
        if (!IsActive)
        {
            return;
        }

        sustainedOutletsByPos[pos.Copy()] = new SustainedOutlet(outletBlock.Id, familyId, Environment.TickCount64 + OutletSustainTtlMs);
    }

    public void UnregisterSustainedOutlet(BlockPos? pos)
    {
        if (pos == null)
        {
            return;
        }

        bool removed = sustainedOutletsByPos.Remove(pos);
        if (removed)
        {
            Block currentFluid = api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            if (currentFluid.Code?.Path.StartsWith("realisticwater-", StringComparison.Ordinal) == true)
            {
                RegisterShutdownDrainOrigin(pos);
                currentFluid.OnNeighbourBlockChange(api.World, pos, pos);
                api.World.BlockAccessor.MarkBlockDirty(pos);
            }
        }
    }

    internal static bool IsRecentShutdownDrainCascadeCell(BlockPos pos)
    {
        long nowMs = Environment.TickCount64;
        PruneExpiredShutdownDrainOrigins(nowMs);

        foreach (ShutdownDrainOrigin origin in shutdownDrainOriginsByPos.Values)
        {
            if (origin.Dimension == pos.dimension &&
                Math.Abs(origin.X - pos.X) + Math.Abs(origin.Y - pos.Y) + Math.Abs(origin.Z - pos.Z) <= ShutdownDrainCascadeRadius)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ShouldSustainOutlet(Block block, IWorldAccessor world, BlockPos pos)
    {
        if (!sustainedOutletsByPos.TryGetValue(pos, out SustainedOutlet outlet))
        {
            return false;
        }

        long nowMs = Environment.TickCount64;
        if (outlet.ExpiresAtMs <= nowMs)
        {
            sustainedOutletsByPos.Remove(pos);
            return false;
        }

        if (!IsExpectedSustainedOutletBlock(block, outlet))
        {
            return false;
        }

        Block currentFluid = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
        return IsExpectedSustainedOutletBlock(currentFluid, outlet);
    }

    private static void RegisterShutdownDrainOrigin(BlockPos pos)
    {
        long nowMs = Environment.TickCount64;
        PruneExpiredShutdownDrainOrigins(nowMs);
        shutdownDrainOriginsByPos[pos.Copy()] = new ShutdownDrainOrigin(
            pos.X,
            pos.Y,
            pos.Z,
            pos.dimension,
            nowMs + ShutdownDrainTtlMs);
    }

    private static void PruneExpiredShutdownDrainOrigins(long nowMs)
    {
        foreach (BlockPos key in shutdownDrainOriginsByPos.Keys.ToArray())
        {
            if (shutdownDrainOriginsByPos[key].ExpiresAtMs <= nowMs)
            {
                shutdownDrainOriginsByPos.Remove(key);
            }
        }
    }

    private static bool IsExpectedSustainedOutletBlock(Block block, SustainedOutlet outlet)
    {
        if (block.LiquidLevel >= 7)
        {
            return false;
        }

        if (string.Equals(outlet.FamilyId, ArchimedesWaterFamilies.Fresh.Id, StringComparison.Ordinal))
        {
            return block.Code?.Path.StartsWith("realisticwater-", StringComparison.Ordinal) == true &&
                   block.LiquidLevel == RealisticWaterSustainedLevel &&
                   string.Equals(block.Variant?["height"], RealisticWaterSustainedLevel.ToString(), StringComparison.Ordinal) &&
                   string.Equals(block.Variant?["sublevel"], RealisticWaterSustainedSublevel.ToString(), StringComparison.Ordinal);
        }

        ArchimedesWaterFamily family = ArchimedesWaterFamilies.GetById(outlet.FamilyId);
        return string.Equals(block.LiquidCode, family.VanillaCode, StringComparison.Ordinal) &&
               block.LiquidLevel == RealisticWaterSustainedLevel;
    }

    public bool IsCompatibleOutletBlock(Block block, string familyId)
    {
        if (!IsActive)
        {
            return false;
        }

        return IsExpectedSustainedOutletBlock(
            block,
            new SustainedOutlet(block.Id, familyId, Environment.TickCount64 + OutletSustainTtlMs));
    }

    private void RefreshPatchState()
    {
        RealisticWaterOutletSustainPatch.Api = api;
        if (!IsActive)
        {
            Unpatch();
            return;
        }

        if (isPatched)
        {
            return;
        }

        harmony.CreateClassProcessor(typeof(RealisticWaterOutletSustainPatch)).Patch();
        if (!RealisticWaterOutletSustainPatch.LastPrepareSucceeded)
        {
            api.Logger.Notification(
                "{0} [compat/realisticwater] Harmony skipped outlet sustain patch (target method not resolved)",
                ArchimedesScrewModSystem.LogPrefix);
            return;
        }

        isPatched = true;
        api.Logger.Notification("{0} [compat/realisticwater] Outlet sustain patch active", ArchimedesScrewModSystem.LogPrefix);
    }

    private void Unpatch()
    {
        if (!isPatched)
        {
            return;
        }

        harmony.UnpatchAll(harmony.Id);
        isPatched = false;
        api.Logger.Notification("{0} [compat/realisticwater] Outlet sustain patch unpatched", ArchimedesScrewModSystem.LogPrefix);
    }

    private bool TryGetBlock(AssetLocation code, out Block block)
    {
        Block? resolved = api.World.GetBlock(code);
        if (resolved == null)
        {
            block = null!;
            return false;
        }

        block = resolved;
        return true;
    }

    private readonly record struct SustainedOutlet(int BlockId, string FamilyId, long ExpiresAtMs);

    private readonly record struct ShutdownDrainOrigin(int X, int Y, int Z, int Dimension, long ExpiresAtMs);
}
