namespace XivSpanish.GameData;

/// <summary>
/// Layout an <see cref="ExdPatcher"/> needs to patch one sheet: the fixed row size, the
/// fixed-data offsets of every String column, and the EXH variant (1 = Default rows,
/// 2 = subrows — each subrow is prefixed by a u16 id, a layout the patcher does NOT support,
/// so variant-2 sheets must be skipped before patching; 0 = unknown for callers that cannot
/// supply it, treated as Default).
/// </summary>
public readonly record struct ExdLayout(int FixedDataSize, IReadOnlyList<int> StringColumnOffsets, int Variant = 1);
