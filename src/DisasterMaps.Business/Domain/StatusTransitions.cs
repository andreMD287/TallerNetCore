namespace DisasterMaps.Business.Domain;

/// <summary>RN-10: REPORTED → VERIFIED → ATTENDED, paso a paso y sin retroceder (decisión R1-03).</summary>
public static class StatusTransitions
{
    public const ImpactStatus Initial = ImpactStatus.Reported;

    public static bool IsAllowed(ImpactStatus from, ImpactStatus to) =>
        to == from || (int)to == (int)from + 1;
}
