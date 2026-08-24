namespace CondotifyAPI.Domain.Enums.Governance;

public enum AssemblyTypeEnum
{
    Poll = 1,
    Ordinary = 2,
    Extraordinary = 3
}

public enum AssemblyFormatEnum
{
    InPerson = 1,
    Virtual = 2,
    Hybrid = 3
}

public enum AssemblyStatusEnum
{
    Draft = 1,
    Published = 2,
    Open = 3,
    Closed = 4,
    Cancelled = 5
}

public enum AssemblyVoteVisibilityEnum
{
    Secret = 1,
    Open = 2
}
