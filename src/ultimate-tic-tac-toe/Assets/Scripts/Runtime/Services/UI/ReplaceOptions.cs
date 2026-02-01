namespace Runtime.Services.UI
{
    public readonly struct ReplaceOptions
    {
        public readonly bool KeepFromVisibleUntilToShown;
        public readonly bool DisableFromInputImmediately;
        public readonly bool CloseFromAfterToOpened;

        public ReplaceOptions(
            bool keepFromVisibleUntilToShown = true,
            bool disableFromInputImmediately = true,
            bool closeFromAfterToOpened = true)
        {
            KeepFromVisibleUntilToShown = keepFromVisibleUntilToShown;
            DisableFromInputImmediately = disableFromInputImmediately;
            CloseFromAfterToOpened = closeFromAfterToOpened;
        }
    }
}
