using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Tests for the tagged union that carries a decoded property value.
/// </summary>
/// <remarks>
/// The point of the tag is that reading a value as the wrong kind throws at the mistake rather
/// than surfacing as a wrong number later. That guarantee is only worth as much as its tests:
/// nearly every value in this format is numeric, so a mismatched read would otherwise return
/// something entirely plausible.
/// </remarks>
public sealed class PropertyValueTests
{
    [Fact]
    public void EachKind_RoundTripsThroughItsOwnAccessor()
    {
        PropertyValue.FromInt(-42).AsInt.ShouldBe(-42);
        PropertyValue.FromFloat(1.5f).AsFloat.ShouldBe(1.5f);
        PropertyValue.FromVector(1f, 2f, 3f).AsVector.ShouldBe((1f, 2f, 3f));
        PropertyValue.FromVectorXY(4f, 5f).AsVectorXY.ShouldBe((4f, 5f));
        PropertyValue.FromString("hello").AsString.ShouldBe("hello");
        PropertyValue.FromArray([PropertyValue.FromInt(7)]).AsArray.ShouldHaveSingleItem()
            .AsInt.ShouldBe(7);
    }

    [Fact]
    public void EachKind_ReportsItself()
    {
        PropertyValue.FromInt(0).Kind.ShouldBe(PropertyValueKind.Int);
        PropertyValue.FromFloat(0f).Kind.ShouldBe(PropertyValueKind.Float);
        PropertyValue.FromVector(0f, 0f, 0f).Kind.ShouldBe(PropertyValueKind.Vector);
        PropertyValue.FromVectorXY(0f, 0f).Kind.ShouldBe(PropertyValueKind.VectorXY);
        PropertyValue.FromString("").Kind.ShouldBe(PropertyValueKind.String);
        PropertyValue.FromArray([]).Kind.ShouldBe(PropertyValueKind.Array);
    }

    [Fact]
    public void ReadingAsTheWrongKind_ThrowsAndNamesBothKinds()
    {
        // A float read as an int is the dangerous case - both are numbers, so without the tag
        // the mistake produces a plausible value rather than a failure.
        InvalidOperationException error =
            Should.Throw<InvalidOperationException>(() => PropertyValue.FromFloat(1.5f).AsInt);

        error.Message.ShouldBe("Property value is Float, not Int.");
    }

    [Fact]
    public void EveryAccessor_RejectsEveryOtherKind()
    {
        // One wrong-kind read per accessor. Without this each accessor's guard is only ever
        // exercised on its success path, where the check itself cannot be observed.
        PropertyValue integer = PropertyValue.FromInt(1);

        Should.Throw<InvalidOperationException>(() => integer.AsFloat);
        Should.Throw<InvalidOperationException>(() => integer.AsVector);
        Should.Throw<InvalidOperationException>(() => integer.AsVectorXY);
        Should.Throw<InvalidOperationException>(() => integer.AsString);
        Should.Throw<InvalidOperationException>(() => integer.AsArray);
        Should.Throw<InvalidOperationException>(() => PropertyValue.FromFloat(1f).AsInt);
    }

    [Fact]
    public void DefaultValue_IsAnIntAndNotAStringOrArray()
    {
        // A default struct has Kind Int with null string and array fields. The accessors must
        // not hand back a null as though it were a value - that is the one path where the tag
        // check alone is not enough.
        PropertyValue uninitialised = default;

        uninitialised.Kind.ShouldBe(PropertyValueKind.Int);
        uninitialised.AsInt.ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => uninitialised.AsString);
        Should.Throw<InvalidOperationException>(() => uninitialised.AsArray);
    }

    [Fact]
    public void NullPayloads_AreNormalisedToEmptyRatherThanStored()
    {
        // The constructor coerces null to empty so the fields are never null whatever the
        // kind, which is what lets the accessors check only the tag. For every other kind that
        // coercion is unobservable - the accessor throws before reading the field - so these
        // two calls are the only way to see it happen at all.
        PropertyValue.FromString(null!).AsString.ShouldBeEmpty();
        PropertyValue.FromArray(null!).AsArray.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PropertyValueKind.Int, "-42")]
    [InlineData(PropertyValueKind.Float, "1.5")]
    [InlineData(PropertyValueKind.Vector, "(1, 2, 3)")]
    [InlineData(PropertyValueKind.VectorXY, "(4, 5)")]
    [InlineData(PropertyValueKind.String, "hello")]
    [InlineData(PropertyValueKind.Array, "[2]")]
    public void ToString_DescribesEachKind(PropertyValueKind kind, string expected)
    {
        // Exact strings, not "contains a digit". This is what a text dump prints, so a format
        // that quietly changed would rewrite every line of output.
        PropertyValue value = kind switch
        {
            PropertyValueKind.Int => PropertyValue.FromInt(-42),
            PropertyValueKind.Float => PropertyValue.FromFloat(1.5f),
            PropertyValueKind.Vector => PropertyValue.FromVector(1f, 2f, 3f),
            PropertyValueKind.VectorXY => PropertyValue.FromVectorXY(4f, 5f),
            PropertyValueKind.String => PropertyValue.FromString("hello"),
            _ => PropertyValue.FromArray(
                [PropertyValue.FromInt(1), PropertyValue.FromInt(2)]),
        };

        value.ToString().ShouldBe(expected);
    }

    [Fact]
    public void ToString_RoundsFloatsToThreeDecimalsAndUsesInvariantCulture()
    {
        // A decimal point, never a comma - the dump has to read the same on any machine.
        PropertyValue.FromFloat(1.23456f).ToString().ShouldBe("1.235");
        PropertyValue.FromVector(-0.5f, 0f, 1234.5f).ToString().ShouldBe("(-0.5, 0, 1234.5)");
    }

    [Fact]
    public void Equality_ComparesKindAndPayload()
    {
        // A record struct, so this is generated - but an int 1 and a float 1 must not compare
        // equal, and that depends on Kind participating.
        PropertyValue.FromInt(1).ShouldBe(PropertyValue.FromInt(1));
        PropertyValue.FromInt(1).ShouldNotBe(PropertyValue.FromFloat(1f));
        PropertyValue.FromVector(1f, 2f, 3f).ShouldNotBe(PropertyValue.FromVector(1f, 2f, 4f));
    }

    [Fact]
    public void NestedArrays_AreCarriedIntact()
    {
        IReadOnlyList<PropertyValue> inner =
            [PropertyValue.FromString("a"), PropertyValue.FromString("b")];

        PropertyValue.FromArray([PropertyValue.FromArray(inner)])
            .AsArray.ShouldHaveSingleItem()
            .AsArray.Count.ShouldBe(2);
    }
}
