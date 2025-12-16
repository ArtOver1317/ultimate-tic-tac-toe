# 03-ScriptableSettings

What it shows:
- Configuring `Log.MinLevel` and muted tags via `ScriptableObject`

How to try:
- Create asset: **Assets → Create → StripLog → Settings**
- Add `StripLogSettingsApplier` to a GameObject
- Assign the settings asset

Note:
- This sample only calls `Log.MuteTag()` for the listed tags; it does not reset previous muted state.
