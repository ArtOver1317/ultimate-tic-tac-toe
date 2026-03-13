#nullable enable

namespace Runtime.Games.Battleship.Networking
{
    internal static class CommonPayloadParts
    {
        public const int CommandId = 1;
        public const int SenderUserId = 2;
    }

    internal static class PlacementPayloadParts
    {
        public const string Prefix = "BP";
        public const int Count = 5;
        public const int LayoutPayload = 3;
        public const int ClientTick = 4;
    }

    internal static class PlacementTimeoutPayloadParts
    {
        public const string Prefix = "BT";
        public const int Count = 6;
        public const int PlayerSlot = 3;
        public const int AutoPlaceSeed = 4;
        public const int ClientTick = 5;
    }

    internal static class RecoveryPayloadParts
    {
        public const string Prefix = "BR";
        public const int Count = 17;
        public const int MatchRoundId = 3;
        public const int Phase = 4;
        public const int ActivePlayerSlot = 5;
        public const int PlacementTimerRemainingMs = 6;
        public const int MoveTimerRemainingMs = 7;
        public const int Player0ConsecutiveTimeouts = 8;
        public const int Player1ConsecutiveTimeouts = 9;
        public const int WinnerSlot = 10;
        public const int FinishStatus = 11;
        public const int ClientTick = 12;
        public const int Player0LayoutPayload = 13;
        public const int Player1LayoutPayload = 14;
        public const int Player0OpponentMarksPayload = 15;
        public const int Player1OpponentMarksPayload = 16;
        public const int MinMatchRoundId = 1;
    }
}