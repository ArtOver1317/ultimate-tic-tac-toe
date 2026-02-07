namespace Runtime.Gameplay.Moves
{
    public static class PlayerMarkExtensions
    {
        public static string ToUiText(this PlayerMark mark) => mark switch
        {
            PlayerMark.X => "X",
            PlayerMark.O => "O",
            _ => string.Empty,
        };
    }
}
