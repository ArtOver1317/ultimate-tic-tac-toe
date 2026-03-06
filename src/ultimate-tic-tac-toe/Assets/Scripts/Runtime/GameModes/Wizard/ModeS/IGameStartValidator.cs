using System.Collections.Generic;

#nullable enable

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Optional cross-field validator for wizard start constraints.
    /// </summary>
    public interface IGameStartValidator
    {
        IReadOnlyList<ValidationError> ValidateForStart(GameSessionSnapshot snapshot);
    }
}
