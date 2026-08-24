namespace AllMyGlams;

public sealed record GlamourerDesignEntry(
    Guid Id,
    string DisplayName,
    string FullPath,
    uint DisplayColor,
    bool ShownInQuickDesignBar);
