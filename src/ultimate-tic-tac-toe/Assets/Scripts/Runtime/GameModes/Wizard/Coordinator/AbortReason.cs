namespace Runtime.GameModes.Wizard.Coordinator
{
    /// <summary>
    /// Reason for aborting the wizard flow.
    /// </summary>
    public enum AbortReason
    {
        UserCancel = 0,
        GameStarted = 1,
        SceneChange = 2,
        Disconnect = 3,
        Error = 4,
        StartCancelled = 5,
    }
}
