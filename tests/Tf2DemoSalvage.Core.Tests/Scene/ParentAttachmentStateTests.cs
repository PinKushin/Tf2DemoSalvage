using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Reading which attachment point an entity hangs from.
/// </summary>
/// <remarks>
/// **Zero is "not attached", not "the first one."** The engine stores attachments one-based —
/// <c>SetupBones_AttachmentHelper</c> ends with <c>PutAttachment( i + 1, world )</c> — so a reader
/// that passed zero through would hang every unattached item off a real point. That is a placement
/// that looks deliberate, which is the worst kind of wrong in this project.
/// </remarks>
public sealed class ParentAttachmentStateTests
{
    [Test]
    public void ParentAttachment_WhenTheEntityNamesOne_IsThatNumber()
    {
        Entity(attachment: 7).ParentAttachment().ShouldBe(7);
    }

    [Test]
    public void ParentAttachment_WhenItIsZero_IsNothing()
    {
        // **The distinguishing case.** Most entities in a demo carry this property at zero, so a
        // reader that treated zero as an index would attach the whole map to somebody's head.
        Entity(attachment: 0).ParentAttachment().ShouldBeNull();
    }

    [Test]
    public void ParentAttachment_WhenTheDemoNeverSaidAnything_IsNothing()
    {
        // A 2007 demo carries the property; an entity that never sent it still answers nothing
        // rather than zero-as-index.
        EntityState bare = new(entityIndex: 4, classId: 1, serialNumber: 1, className: "CTFWearable");

        bare.ParentAttachment().ShouldBeNull();
    }

    private static EntityState Entity(int attachment)
    {
        EntityState state = new(entityIndex: 4, classId: 1, serialNumber: 1, className: "CTFWearable");

        state.Set("DT_BaseEntity.m_iParentAttachment", PropertyValue.FromInt(attachment));

        return state;
    }
}
