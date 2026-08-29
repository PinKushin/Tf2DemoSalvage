using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// What the SourceTV director asked the camera to do — one <c>hltv_chase</c> event.
/// </summary>
/// <param name="InEye">
/// Whether to watch through the target's eyes rather than from behind: the event's <c>ineye</c>.
/// </param>
/// <param name="Target">Whose shot this is — <c>target1</c>.</param>
/// <param name="SecondTarget">
/// The player the camera should look TOWARDS, or zero when there is none. <c>target2</c>.
/// </param>
/// <param name="Distance">How far back the chase camera sits — <c>m_flDistance</c>.</param>
/// <param name="Offset">How far the point looked at is raised — <c>m_flOffset</c>.</param>
/// <param name="Theta">The yaw the camera is swung round the target by — <c>m_flTheta</c>.</param>
/// <param name="Phi">The pitch it looks down by — <c>m_flPhi</c>.</param>
/// <remarks>
/// **This is the director's half of a SourceTV recording and this project ignored all of it.** The
/// engine reads it in <c>C_HLTVCamera::FireGameEvent</c> (<c>hltvcamera.cpp:776</c>):
///
/// <code>
///   bool bInEye = event->GetInt( "ineye" );
///   …
///   SetMode( bInEye ? OBS_MODE_IN_EYE : OBS_MODE_CHASE );
///   m_iTraget2    = event->GetInt( "target2" );
///   m_flDistance  = event->GetFloat( "distance", m_flDistance );
///   m_flOffset    = event->GetFloat( "offset", m_flOffset );
///   m_flTheta     = event->GetFloat( "theta", m_flTheta );
///   m_flPhi       = event->GetFloat( "phi", m_flPhi );
/// </code>
///
/// **Every numeric field falls back to its PREVIOUS value, not to a constant**, which is why
/// <see cref="From"/> takes the shot before it. An event naming only <c>distance</c> leaves the
/// angles where the last one put them, and treating the absent fields as zero would snap the camera
/// straight every time the director pulled back.
/// </remarks>
public readonly record struct DirectorShot(
    bool InEye,
    int Target,
    int SecondTarget,
    float Distance,
    float Offset,
    float Theta,
    float Phi)
{
    /// <summary>The event's name on the wire.</summary>
    public const string ChaseEvent = "hltv_chase";

    /// <summary>Reads one event, carrying forward what it does not mention.</summary>
    /// <param name="values">The event's fields, as decoded.</param>
    /// <param name="previous">The shot before this one, or null for the first.</param>
    /// <returns>The shot.</returns>
    /// <remarks>
    /// **`ineye` and the targets do NOT carry forward, and the numbers do.** That asymmetry is the
    /// engine's: it reads the two targets and the mode with plain <c>GetInt</c>, which answers zero
    /// for a field the event omits, while every float is read with an explicit default of the
    /// current value.
    /// </remarks>
    /// <exception cref="System.ArgumentNullException"><paramref name="values"/> is null.</exception>
    public static DirectorShot From(
        IReadOnlyDictionary<string, object?> values, DirectorShot? previous)
    {
        System.ArgumentNullException.ThrowIfNull(values);

        DirectorShot last = previous ?? Default;

        return new DirectorShot(
            Number(values, "ineye", 0f) != 0f,
            (int)Number(values, "target1", 0f),
            (int)Number(values, "target2", 0f),
            Number(values, "distance", last.Distance),
            Number(values, "offset", last.Offset),
            Number(values, "theta", last.Theta),
            Number(values, "phi", last.Phi));
    }

    /// <summary>What <c>C_HLTVCamera::Reset</c> leaves the chase parameters at.</summary>
    /// <remarks><c>m_flDistance = 96</c>, and phi, theta and offset all zero.</remarks>
    public static DirectorShot Default { get; } =
        new(InEye: false, Target: 0, SecondTarget: 0, Distance: 96f, Offset: 0f, Theta: 0f, Phi: 0f);

    /// <summary>One field, as a number, whatever numeric type the definition gave it.</summary>
    /// <remarks>
    /// **A game event's fields are typed by their DEFINITION, so the same name is not always the
    /// same CLR type.** `hltv_chase` declares its targets as `short` and its parameters as `float`,
    /// and reading either through a single `is int` test is how this project has already shipped
    /// two silent no-ops — a dump annotation that matched nothing because `customkill` arrives as a
    /// byte, and a kill feed whose numeric lookup was handed strings.
    /// </remarks>
    private static float Number(
        IReadOnlyDictionary<string, object?> values, string field, float fallback)
    {
        if (!values.TryGetValue(field, out object? value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            float number => number,
            double number => (float)number,
            int number => number,
            short number => number,
            byte number => number,
            bool set => set ? 1f : 0f,
            string text when float.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) => parsed,
            _ => fallback,
        };
    }
}
