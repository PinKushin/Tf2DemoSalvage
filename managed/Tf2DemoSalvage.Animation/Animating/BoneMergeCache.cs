using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// Which of a worn model's bones come from its wearer, matched by name.
/// </summary>
/// <remarks>
/// **This is <c>CBoneMergeCache</c> (<c>game/client/bone_merge_cache.cpp</c>), and it is where the
/// engine solves the ordering problem this project used to solve with a sort.** The merge does not
/// wait to be told its parent is ready — it asks:
///
/// <code>
/// // Have the entity we're following setup its bones.
/// bool bWorked = m_pFollow->SetupBones( NULL, -1, m_nFollowBoneSetupMask, gpGlobals->curtime );
/// </code>
///
/// at <c>:130</c>. That call is the whole of Valve's ordering, and
/// <see cref="AnimatingEntity.SetupBones(int, double)"/> being idempotent is what makes it cheap.
///
/// **The pairing is cached because it depends only on the two skeletons**, never on the frame, and
/// re-derived when either model changes — a hat worn by a scout and then by a heavy is a different
/// pairing, and using the scout's on the heavy puts it a bone or two out, which reads as a hat
/// sitting slightly wrong rather than as a bug.
/// </remarks>
public sealed class BoneMergeCache
{
    private readonly IBonePose _worn;

    private IBonePose? _pairedTo;
    private (int Mine, int Theirs)[] _merged = [];
    private bool[] _isMerged = [];

    /// <summary>Creates a cache for one worn model.</summary>
    /// <param name="worn">The item's own pose source, whose bone names decide the pairing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="worn"/> is null.</exception>
    public BoneMergeCache(IBonePose worn)
    {
        ArgumentNullException.ThrowIfNull(worn);

        _worn = worn;
    }

    /// <summary>
    /// Which bones the wearer must build for this merge to work.
    /// </summary>
    /// <remarks>
    /// **Starts at <c>BONE_USED_BY_BONE_MERGE</c> and widens to <c>BONE_USED_BY_ANYTHING</c> the
    /// moment one matched parent bone is not marked** (<c>bone_merge_cache.cpp:95</c>). Valve's own
    /// warning beside that line is commented out, and it calls this a *performance* warning rather
    /// than an error — which is exactly right, and exactly why it is worth exposing here.
    ///
    /// An unmarked bone does not break the merge. It makes the WEARER build its entire skeleton,
    /// for every item worn on it. Measured 2026-08-24: TF2 does mark its merge bones — scout 42 of
    /// 78, heavy 30 of 79 — so this stays narrow on the models this viewer draws, and a future
    /// model that does not is a cost with a name rather than an unexplained slowdown.
    /// </remarks>
    public int FollowBoneSetupMask { get; private set; }

    /// <summary>How many of the worn model's bones found a counterpart.</summary>
    public int MatchedCount => _merged.Length;

    /// <summary>Whether a bone of the worn model is supplied by the wearer.</summary>
    /// <param name="bone">Which of the worn model's bones.</param>
    /// <returns>Whether the merge writes it.</returns>
    /// <remarks>
    /// <c>BuildTransformations</c> skips exactly these (<c>c_baseanimating.cpp:1519</c>): they are
    /// already in the array, and rebuilding one from the item's own animation would undo the merge.
    /// </remarks>
    public bool IsMerged(int bone) =>
        bone >= 0 && bone < _isMerged.Length && _isMerged[bone];

    /// <summary>Re-pairs the two skeletons when either model has changed.</summary>
    /// <param name="wearer">What this item is worn on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wearer"/> is null.</exception>
    public void UpdateCache(IBonePose wearer)
    {
        ArgumentNullException.ThrowIfNull(wearer);

        if (ReferenceEquals(_pairedTo, wearer))
        {
            return;
        }

        _pairedTo = wearer;
        _isMerged = new bool[_worn.BoneCount];

        Dictionary<string, int> theirs = new(StringComparer.OrdinalIgnoreCase);

        for (int bone = 0; bone < wearer.BoneCount; bone++)
        {
            // First wins, matching Studio_BoneIndexByName, which returns the first hit. A skeleton
            // with two bones of one name is malformed; taking the last would differ from the engine
            // on exactly the files where behaviour is hardest to predict.
            theirs.TryAdd(wearer.NameOf(bone), bone);
        }

        List<(int Mine, int Theirs)> pairs = [];
        int mask = StudioBoneFlags.UsedByBoneMerge;

        for (int bone = 0; bone < _worn.BoneCount; bone++)
        {
            if (!theirs.TryGetValue(_worn.NameOf(bone), out int match))
            {
                continue;
            }

            pairs.Add((bone, match));
            _isMerged[bone] = true;

            if ((wearer.FlagsOf(match) & StudioBoneFlags.UsedByBoneMerge) == 0)
            {
                mask = StudioBoneFlags.UsedByAnything;
            }
        }

        _merged = [.. pairs];

        // Valve slams the mask to zero when nothing matched, so a chain of unrelated models does
        // not make its parent build anything at all.
        FollowBoneSetupMask = pairs.Count == 0 ? 0 : mask;

        Report(wearer, pairs);
    }

    /// <summary>Says what paired, once per pairing.</summary>
    /// <remarks>
    /// **Restored on 2026-08-24 after being deleted with the code it lived in.** The old
    /// <c>EntityModelSet.Merge</c> logged <c>bone merge X onto Y: N of M bones matched</c>, and D88
    /// removed the method and the line together. The very next viewer run showed weapons in the
    /// wrong place, and the log could not say whether they had paired — the diagnostic for the
    /// thing that broke was removed by the change that broke it.
    ///
    /// **A count that matches nothing looks identical to one that works**: both draw the item, only
    /// one puts it on the wearer. The names matter as much as the number — an item matching 1 bone
    /// of 8 is correct when that one is <c>bip_head</c> and is an item on the floor when it is a
    /// root both skeletons happen to share.
    ///
    /// Once per pairing rather than per frame, because <see cref="UpdateCache"/> only runs when the
    /// followed entity or either model changes.
    /// </remarks>
    private void Report(IBonePose wearer, List<(int Mine, int Theirs)> pairs)
    {
        if (_log is null)
        {
            return;
        }

        string matched = pairs.Count == 0
            ? "nothing"
            : string.Join(", ", pairs.Take(6).Select(pair => _worn.NameOf(pair.Mine)));

        _log.LogInformation(
            "{Message}",
            $"bone merge: {pairs.Count} of {_worn.BoneCount} bones matched onto a " +
            $"{wearer.BoneCount}-bone wearer; matched {matched}; " +
            $"wearer setup mask 0x{FollowBoneSetupMask:X}");
    }

    /// <summary>Where pairings are reported, or null for nowhere.</summary>
    private ILogger? _log;

    /// <summary>Sets where this reports, for a caller that has a logger to give it.</summary>
    /// <param name="log">The logger, under <c>render</c> as the old line was.</param>
    /// <remarks>
    /// A property rather than a constructor argument because the cache is built lazily by
    /// <see cref="AnimatingEntity"/> at the moment a merge first happens, and that is a place with
    /// no logger to hand. Optional, so every test that builds one gets geometry rather than
    /// commentary.
    /// </remarks>
    public void ReportsTo(ILogger? log) => _log = log;

    /// <summary>Copies the wearer's matched matrices into the worn model's bone array.</summary>
    /// <param name="wearerBones">The wearer's finished bones.</param>
    /// <param name="into">The worn model's array, written in place.</param>
    /// <param name="marked">Marked for each bone written, so the transform stage skips it.</param>
    /// <param name="boneMask">Which bones are wanted; others are left alone.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Into the SAME array the transform stage then builds the rest from**, which is the whole of
    /// B180. The engine has one bone array per entity: the merge writes with
    /// <c>GetBoneForWrite</c> (<c>bone_merge_cache.cpp:167</c>), and every unmerged bone is then
    /// built from <c>GetBone( parent )</c> out of that same array
    /// (<c>c_baseanimating.cpp:1595</c>) — so a bone whose parent was merged rides the merged
    /// position with nothing extra written to make it happen.
    ///
    /// The arrangement this replaces kept two arrays and recorded the wrong one, so a chained child
    /// merged onto its parent's UNMERGED bones. Here that state does not exist to record.
    /// </remarks>
    public void MergeMatchingBones(
        BoneAccessor wearerBones, BoneAccessor into, BoneBitList marked, int boneMask)
    {
        ArgumentNullException.ThrowIfNull(wearerBones);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(marked);

        foreach ((int mine, int theirs) in _merged)
        {
            if ((_worn.FlagsOf(mine) & boneMask) == 0)
            {
                continue;
            }

            wearerBones.Bone(theirs).CopyTo(into.BoneForWrite(mine));
            marked.Mark(mine);
        }
    }

    /// <summary>Collapses every merged bone to nothing, for a wearer that could not be built.</summary>
    /// <param name="into">The worn model's array.</param>
    /// <param name="marked">Marked as written, so nothing rebuilds them.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Valve's own fallback, and its comment says why** (<c>bone_merge_cache.cpp:136</c>):
    /// *"Usually this means your parent is invisible or gone or whatever. This routine has no way to
    /// tell its caller not to draw itself unfortunately. But we can shrink all the bones down to
    /// zero size."*
    ///
    /// A zero-scaled matrix at the origin draws nothing visible, which is the point — the
    /// alternative is an item at full size in the middle of the map. This project can usually do
    /// better, because <see cref="AnimatingEntity.SetupBones(int, double)"/> returns false and the
    /// caller declines to draw at all; this exists for the case where something drew anyway.
    /// </remarks>
    public void ShrinkToNothing(BoneAccessor into, BoneBitList marked)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(marked);

        foreach ((int mine, int _) in _merged)
        {
            float[] bone = into.BoneForWrite(mine);

            Array.Clear(bone);
            marked.Mark(mine);
        }
    }
}
