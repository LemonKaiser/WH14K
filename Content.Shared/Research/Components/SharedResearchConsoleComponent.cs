using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components
{
    [NetSerializable, Serializable]
    public enum ResearchConsoleUiKey : byte
    {
        Key,
    }

    [Serializable, NetSerializable]
    public sealed class ConsoleUnlockTechnologyMessage : BoundUserInterfaceMessage
    {
        public string Id;

        public ConsoleUnlockTechnologyMessage(string id)
        {
            Id = id;
        }
    }

    [Serializable, NetSerializable]
    public sealed class ConsoleServerSelectionMessage : BoundUserInterfaceMessage
    {

    }

    [Serializable, NetSerializable]
    public sealed class ResearchConsoleBoundInterfaceState : BoundUserInterfaceState
    {
        public int Points;
        public bool TimedResearchEnabled;
        public string? ActiveTechnologyId;
        public int ActiveTechnologyRemainingSeconds;

        public ResearchConsoleBoundInterfaceState(
            int points,
            bool timedResearchEnabled,
            string? activeTechnologyId,
            int activeTechnologyRemainingSeconds)
        {
            Points = points;
            TimedResearchEnabled = timedResearchEnabled;
            ActiveTechnologyId = activeTechnologyId;
            ActiveTechnologyRemainingSeconds = activeTechnologyRemainingSeconds;
        }
    }
}
