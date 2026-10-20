namespace Snapr.Core.Recording;

public enum MicAttempt { Chosen, Default, None }

/// <summary>Which microphone to open next (spec parity 20 §2.1): the chosen one, then the default once, then none.</summary>
public static class MicChoice
{
    public static MicAttempt Next(string? chosenId, bool chosenFailed) =>
        chosenId is null ? (chosenFailed ? MicAttempt.None : MicAttempt.Default)
        : chosenFailed ? MicAttempt.Default : MicAttempt.Chosen;
}
