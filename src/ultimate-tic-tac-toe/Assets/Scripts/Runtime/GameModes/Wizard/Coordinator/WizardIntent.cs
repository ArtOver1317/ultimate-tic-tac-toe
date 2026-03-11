namespace Runtime.GameModes.Wizard.Coordinator
{
    /// <summary>
    /// High-level navigation intents produced by wizard view-models.
    /// Processed by <see cref="IGameWizardCoordinator"/>.
    /// </summary>
    public enum WizardIntent
    {
        Continue = 0,
        Back = 1,
        Cancel = 2,
        Start = 3,
    }
}
