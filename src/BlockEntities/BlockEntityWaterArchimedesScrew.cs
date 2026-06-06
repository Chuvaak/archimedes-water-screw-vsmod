using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ArchimedesScrew;

/// <summary>
/// Server-side controller for a water Archimedes screw assembly. When the assembly is valid,
/// mechanically powered, and the intake sits in supported water, it maintains a single regular
/// level-7 source block at the output (matching the intake fluid family). The source is removed
/// as soon as the assembly is no longer functional.
/// </summary>
public sealed class BlockEntityWaterArchimedesScrew : BlockEntity
{
    private const string PlacedXKey = "placedOutputX";
    private const string PlacedYKey = "placedOutputY";
    private const string PlacedZKey = "placedOutputZ";
    private const string PlacedDimKey = "placedOutputDim";
    private const string PlacedBlockIdKey = "placedOutputBlockId";
    private const string PlacedFamilyKey = "placedOutputFamily";

    private const string RealisticWaterPrefix = "realisticwater-";

    private const int TickIntervalMs = 250;
    private const float MinimumNetworkSpeed = 0.001f;

    private ArchimedesScrewModSystem? modSystem;
    private long tickListenerId;

    private BlockPos? placedOutputPos;
    private int placedOutputBlockId;
    private string? placedOutputFamilyId;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        modSystem = api.ModLoader.GetModSystem<ArchimedesScrewModSystem>();
        UpdateTickRegistration();
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        UpdateTickRegistration();
    }

    public override void OnBlockRemoved()
    {
        RemovePlacedOutput("block removed");
        UnregisterTick();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        // Chunk unloading is not assembly teardown: leave any placed source in the world.
        // Realistic-water sustain entries simply expire from their in-memory TTL.
        UnregisterTick();
        base.OnBlockUnloaded();
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        if (tree.HasAttribute(PlacedBlockIdKey))
        {
            placedOutputPos = new BlockPos(
                tree.GetInt(PlacedXKey),
                tree.GetInt(PlacedYKey),
                tree.GetInt(PlacedZKey),
                tree.GetInt(PlacedDimKey));
            placedOutputBlockId = tree.GetInt(PlacedBlockIdKey);
            placedOutputFamilyId = tree.GetString(PlacedFamilyKey);
        }
        else
        {
            placedOutputPos = null;
            placedOutputBlockId = 0;
            placedOutputFamilyId = null;
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        if (placedOutputPos != null)
        {
            tree.SetInt(PlacedXKey, placedOutputPos.X);
            tree.SetInt(PlacedYKey, placedOutputPos.Y);
            tree.SetInt(PlacedZKey, placedOutputPos.Z);
            tree.SetInt(PlacedDimKey, placedOutputPos.dimension);
            tree.SetInt(PlacedBlockIdKey, placedOutputBlockId);
            tree.SetString(PlacedFamilyKey, placedOutputFamilyId ?? string.Empty);
        }
        else
        {
            tree.RemoveAttribute(PlacedXKey);
            tree.RemoveAttribute(PlacedYKey);
            tree.RemoveAttribute(PlacedZKey);
            tree.RemoveAttribute(PlacedDimKey);
            tree.RemoveAttribute(PlacedBlockIdKey);
            tree.RemoveAttribute(PlacedFamilyKey);
        }
    }

    private void UpdateTickRegistration()
    {
        if (Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (Block is BlockWaterArchimedesScrew screw && screw.IsIntakeBlock())
        {
            if (tickListenerId == 0)
            {
                tickListenerId = Api.Event.RegisterGameTickListener(OnTick, TickIntervalMs);
            }
        }
        else
        {
            UnregisterTick();
        }
    }

    private void UnregisterTick()
    {
        if (Api != null && tickListenerId != 0)
        {
            Api.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
    }

    private void OnTick(float dt)
    {
        if (Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        if (Block is not BlockWaterArchimedesScrew screw || !screw.IsIntakeBlock())
        {
            RemovePlacedOutput("no longer an intake block");
            UpdateTickRegistration();
            return;
        }

        ArchimedesScrewAssemblyAnalyzer.AssemblyStatus status =
            ArchimedesScrewAssemblyAnalyzer.Analyze(Api.World, Pos, MinimumNetworkSpeed);

        if (status is { IsFunctional: true, OutputPos: { } outputPos } &&
            TryResolveIntakeFamily(out string familyId))
        {
            if (placedOutputPos != null && !placedOutputPos.Equals(outputPos))
            {
                RemovePlacedOutput("output position changed");
            }

            EnsureOutput(outputPos.Copy(), familyId);
        }
        else
        {
            RemovePlacedOutput("assembly not functional");
        }
    }

    private bool TryResolveIntakeFamily(out string familyId)
    {
        Block intakeFluid = Api!.World.BlockAccessor.GetBlock(Pos, BlockLayersAccess.Fluid);
        if (ArchimedesWaterFamilies.TryResolveVanillaFamily(intakeFluid, out ArchimedesWaterFamily family))
        {
            familyId = family.Id;
            return true;
        }

        if (modSystem != null && modSystem.TryResolveRealisticWaterIntakeFamily(intakeFluid, out familyId))
        {
            return true;
        }

        familyId = string.Empty;
        return false;
    }

    private void EnsureOutput(BlockPos outputPos, string familyId)
    {
        if (Api == null)
        {
            return;
        }

        IBlockAccessor accessor = Api.World.BlockAccessor;

        // RealisticWater compat: place the realistic outlet block and keep it sustained.
        // Only valid when the resolved block is an actual realistic-water block (the sustain
        // Harmony patch only targets RealisticWater's behavior). For non-realistic resolutions
        // (e.g. salt/boiling families, or a missing block) fall through to a vanilla level-7 source.
        if (modSystem != null &&
            modSystem.IsRealisticWaterCompatActive &&
            modSystem.TryResolveRealisticWaterOutletBlock(familyId, out Block outletBlock) &&
            outletBlock.Code?.Path.StartsWith(RealisticWaterPrefix, StringComparison.Ordinal) == true)
        {
            Block currentFluid = accessor.GetBlock(outputPos, BlockLayersAccess.Fluid);
            if (currentFluid.Id != outletBlock.Id &&
                !modSystem.IsCompatibleRealisticWaterOutletBlock(currentFluid, familyId))
            {
                accessor.SetBlock(outletBlock.Id, outputPos, BlockLayersAccess.Fluid);
                TriggerFluidUpdates(outputPos, outletBlock);
            }

            modSystem.RefreshRealisticWaterSustainedOutlet(outputPos, familyId, outletBlock);
            SetPlacedOutput(outputPos, outletBlock.Id, familyId);
            return;
        }

        // Default: maintain a level-7 source of the matching family. Only replace when the cell is
        // empty, the family differs, or the liquid height is below 7. A level-7 block of the correct
        // family is left alone regardless of flow state (still, flowing, falling, etc.).
        ArchimedesWaterFamily resolvedFamily = ArchimedesWaterFamilies.GetById(familyId);
        Block? sourceBlock = Api.World.GetBlock(resolvedFamily.SourceBlockCode);
        if (sourceBlock == null)
        {
            return;
        }

        Block existingFluid = accessor.GetBlock(outputPos, BlockLayersAccess.Fluid);
        if (!IsVanillaFamilyAtHeight7(existingFluid, familyId))
        {
            accessor.SetBlock(sourceBlock.Id, outputPos, BlockLayersAccess.Fluid);
            TriggerFluidUpdates(outputPos, sourceBlock);
            SetPlacedOutput(outputPos, sourceBlock.Id, familyId);
            return;
        }

        SetPlacedOutput(outputPos, existingFluid.Id, familyId);
    }

    private static bool IsVanillaFamilyAtHeight7(Block fluid, string familyId)
    {
        if (fluid.Id == 0 || fluid.LiquidLevel != 7)
        {
            return false;
        }

        return ArchimedesWaterFamilies.TryResolveVanillaFamily(fluid, out ArchimedesWaterFamily family) &&
               string.Equals(family.Id, familyId, StringComparison.Ordinal);
    }

    private void RemovePlacedOutput(string reason)
    {
        if (placedOutputPos == null || Api == null)
        {
            placedOutputPos = null;
            placedOutputBlockId = 0;
            placedOutputFamilyId = null;
            return;
        }

        BlockPos pos = placedOutputPos;

        // No-op when no realistic outlet is registered here; otherwise triggers the realistic drain cascade.
        modSystem?.UnregisterRealisticWaterSustainedOutlet(pos);

        Block currentFluid = Api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
        bool isRealistic = currentFluid.Code?.Path.StartsWith(RealisticWaterPrefix, StringComparison.Ordinal) == true;
        if (!isRealistic && ShouldRemoveOutputFluid(currentFluid))
        {
            Api.World.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Fluid);
            TriggerFluidUpdates(pos, currentFluid);
        }

        placedOutputPos = null;
        placedOutputBlockId = 0;
        placedOutputFamilyId = null;
        MarkDirty();
    }

    /// <summary>
    /// Matches the managed output by water family rather than by exact block id: vanilla water
    /// simulation can swap the flow/height variant at the output cell (e.g. still-7 to a flowing or
    /// falling variant), but it is still our managed source and must be removed on invalidation.
    /// </summary>
    private bool ShouldRemoveOutputFluid(Block currentFluid)
    {
        if (currentFluid.Id == 0)
        {
            return false;
        }

        if (!ArchimedesWaterFamilies.TryResolveVanillaFamily(currentFluid, out ArchimedesWaterFamily family))
        {
            return false;
        }

        return string.IsNullOrEmpty(placedOutputFamilyId) ||
               string.Equals(family.Id, placedOutputFamilyId, StringComparison.Ordinal);
    }

    private void SetPlacedOutput(BlockPos outputPos, int blockId, string familyId)
    {
        bool changed = placedOutputPos == null ||
                       !placedOutputPos.Equals(outputPos) ||
                       placedOutputBlockId != blockId ||
                       !string.Equals(placedOutputFamilyId, familyId, StringComparison.Ordinal);
        placedOutputPos = outputPos;
        placedOutputBlockId = blockId;
        placedOutputFamilyId = familyId;
        if (changed)
        {
            MarkDirty();
        }
    }

    private void TriggerFluidUpdates(BlockPos pos, Block placedFluid)
    {
        if (Api == null)
        {
            return;
        }

        IBlockAccessor accessor = Api.World.BlockAccessor;
        accessor.TriggerNeighbourBlockUpdate(pos);
        accessor.MarkBlockDirty(pos);
        placedFluid.OnNeighbourBlockChange(Api.World, pos, pos);

        BlockPos neighbourPos = new(0);
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            neighbourPos.Set(pos.X + face.Normali.X, pos.Y + face.Normali.Y, pos.Z + face.Normali.Z);

            Block neighbourSolid = accessor.GetBlock(neighbourPos);
            if (neighbourSolid.Id != 0)
            {
                neighbourSolid.OnNeighbourBlockChange(Api.World, neighbourPos, pos);
            }

            Block neighbourFluid = accessor.GetBlock(neighbourPos, BlockLayersAccess.Fluid);
            if (neighbourFluid.Id != 0)
            {
                neighbourFluid.OnNeighbourBlockChange(Api.World, neighbourPos, pos);
            }
        }
    }
}
