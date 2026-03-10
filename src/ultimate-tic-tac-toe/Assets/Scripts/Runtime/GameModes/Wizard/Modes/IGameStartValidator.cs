#nullable enable

using System.Collections.Generic;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard.Modes
{
    /// <summary>
    /// Optional cross-field validator for wizard start constraints.
    /// </summary>
    public interface IGameStartValidator
    {
        IReadOnlyList<ValidationError> ValidateForStart(GameSessionSnapshot snapshot);
    }
}
