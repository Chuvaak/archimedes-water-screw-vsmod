using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ArchimedesScrew;

public sealed class ArchimedesScrewModSystem : ModSystem
{
    public const string LogPrefix = "[archimedes_screw]";
    public const string ModId = "archimedes_screw";
    public const string ScrewBlockCode = "water-archimedesscrew";

    private RealisticWaterCompatBridge? realisticWaterCompatBridge;

    public bool IsRealisticWaterCompatActive => realisticWaterCompatBridge?.IsActive ?? false;

    public bool TryResolveRealisticWaterOutletBlock(string familyId, out Block outletBlock)
    {
        outletBlock = null!;
        return realisticWaterCompatBridge?.TryResolveOutletBlock(familyId, out outletBlock) == true;
    }

    public bool IsCompatibleRealisticWaterOutletBlock(Block block, string familyId)
    {
        return realisticWaterCompatBridge?.IsCompatibleOutletBlock(block, familyId) == true;
    }

    public bool TryResolveRealisticWaterIntakeFamily(Block block, out string familyId)
    {
        familyId = string.Empty;
        return realisticWaterCompatBridge?.TryResolveIntakeFamily(block, out familyId) == true;
    }

    public void RefreshRealisticWaterSustainedOutlet(BlockPos pos, string familyId, Block outletBlock)
    {
        realisticWaterCompatBridge?.RefreshSustainedOutlet(pos, familyId, outletBlock);
    }

    public void UnregisterRealisticWaterSustainedOutlet(BlockPos? pos)
    {
        realisticWaterCompatBridge?.UnregisterSustainedOutlet(pos);
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass(nameof(BlockWaterArchimedesScrew), typeof(BlockWaterArchimedesScrew));
        api.RegisterBlockEntityClass(nameof(BlockEntityWaterArchimedesScrew), typeof(BlockEntityWaterArchimedesScrew));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        realisticWaterCompatBridge = new RealisticWaterCompatBridge(api);
        api.Logger.Notification("{0} Server side initialized", LogPrefix);
    }

    public override void Dispose()
    {
        realisticWaterCompatBridge?.Dispose();
        realisticWaterCompatBridge = null;
        base.Dispose();
    }

    public static void LogVerboseOrNotification(ILogger? logger, string message, params object?[] args)
    {
        logger?.Notification(message, args);
    }
}
