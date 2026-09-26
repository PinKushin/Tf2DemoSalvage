using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A `team_round_timer` — `C_TeamRoundTimer`, as `DT_TeamRoundTimer` sends it (teamplay_round_timer.cpp:139).</summary>
/// <param name="EntityIndex">Its entity slot, which `m_iTimerToShowInHUD` names.</param>
public readonly record struct SceneRoundTimer(int EntityIndex)
{
    /// <summary>`RT_STATE_SETUP` (shareddefs.h:917).</summary>
    public const int StateSetup = 0;

    /// <summary>`RT_STATE_NORMAL`.</summary>
    public const int StateNormal = 1;

    /// <summary>`m_bTimerPaused`.</summary>
    public bool Paused { get; init; }

    /// <summary>`m_flTimeRemaining`: the seconds left when it paused.</summary>
    public float TimeRemaining { get; init; }

    /// <summary>`m_flTimerEndTime`, on the server's clock.</summary>
    public float EndTime { get; init; }

    /// <summary>`m_nTimerMaxLength`.</summary>
    public int MaxLength { get; init; }

    /// <summary>`m_bIsDisabled`.</summary>
    public bool Disabled { get; init; }

    /// <summary>`m_bShowInHUD`.</summary>
    public bool ShowInHud { get; init; }

    /// <summary>`m_nTimerLength`.</summary>
    public int Length { get; init; }

    /// <summary>`m_nSetupTimeLength`.</summary>
    public int SetupLength { get; init; }

    /// <summary>`m_nState`: <see cref="StateSetup"/> or <see cref="StateNormal"/>.</summary>
    public int State { get; init; }

    /// <summary>`m_bShowTimeRemaining`: the time left, rather than the time passed.</summary>
    public bool ShowTimeRemaining { get; init; }

    /// <summary>`m_bInCaptureWatchState`.</summary>
    public bool CaptureWatch { get; init; }

    /// <summary>`m_bStopWatchTimer`.</summary>
    public bool StopWatch { get; init; }

    /// <summary>`m_flTotalTime`.</summary>
    public float TotalTime { get; init; }

    /// <summary>`GetTimerMaxLength` (:417): the setup length in setup, else the maximum, else the length.</summary>
    public int TimerMaxLength
    {
        get
        {
            if (State == StateSetup)
            {
                return SetupLength;
            }

            return MaxLength != 0 ? MaxLength : Length;
        }
    }

    /// <summary>`IsRoundMaxTimerSet`.</summary>
    public bool RoundMaxTimerSet => MaxLength > 0;

    /// <summary>`GetTimeRemaining` (:377): the seconds left, paused or not, never below zero.</summary>
    /// <param name="curTime">`gpGlobals->curtime`, on the server's clock.</param>
    public float TimeRemainingAt(float curTime)
    {
        float remaining;

        if (StopWatch && CaptureWatch)
        {
            remaining = TotalTime;
        }
        else
        {
            remaining = Paused ? TimeRemaining : EndTime - curTime;
        }

        return Math.Max(remaining, 0f);
    }
}
