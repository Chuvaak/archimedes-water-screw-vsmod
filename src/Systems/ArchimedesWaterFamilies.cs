using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ArchimedesScrew;

public readonly record struct ArchimedesWaterFamily(string Id, string VanillaCode)
{
    public string VanillaPrefix => VanillaCode + "-";

    /// <summary>Asset location of the regular vanilla level-7 source block for this family.</summary>
    public AssetLocation SourceBlockCode => new("game", $"{VanillaCode}-still-7");
}

public static class ArchimedesWaterFamilies
{
    public static readonly ArchimedesWaterFamily Fresh = new("fresh", "water");
    public static readonly ArchimedesWaterFamily Salt = new("salt", "saltwater");
    public static readonly ArchimedesWaterFamily Boiling = new("boiling", "boilingwater");

    public static IReadOnlyList<ArchimedesWaterFamily> All { get; } = new[]
    {
        Fresh,
        Salt,
        Boiling
    };

    public static bool TryResolveVanillaFamily(Block block, out ArchimedesWaterFamily family)
    {
        string? path = block.Code?.Path;
        if (path != null)
        {
            foreach (ArchimedesWaterFamily candidate in All)
            {
                if (path.StartsWith(candidate.VanillaPrefix, StringComparison.Ordinal))
                {
                    family = candidate;
                    return true;
                }
            }
        }

        family = default;
        return false;
    }

    public static ArchimedesWaterFamily GetById(string familyId)
    {
        foreach (ArchimedesWaterFamily family in All)
        {
            if (string.Equals(family.Id, familyId, StringComparison.Ordinal))
            {
                return family;
            }
        }

        string validIds = string.Join(", ", All.Select(f => f.Id));
        throw new InvalidOperationException(
            $"No Archimedes water family with id '{familyId}'. Valid ids: {validIds}");
    }
}
