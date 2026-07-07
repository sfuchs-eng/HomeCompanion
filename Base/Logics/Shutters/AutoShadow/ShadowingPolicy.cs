namespace HomeCompanion.Logics.Shutters;

/// <summary>
/// Results from the thermal assessment of the building and its rooms/room shutter scene, room temp, ...
/// </summary>
public enum ShadowingPolicy
{
    /// <summary>
    /// Preference is open shutters from a thermal perspective, i.e. we want to avoid shadowing if possible, e.g. to allow solar gain for heating the building.
    /// </summary>
    NoShadowing,

    /// <summary>
    /// Preference is to avoid shadowing if possible, but if there's a user preference for shadowing it's tolerated.
    /// Priority is on least shutter movement
    /// </summary>
    AvoidShadowing,

    /// <summary>
    /// Shadow in case there's no user preference for dailight or if the user has already requested shadowing.
    /// Priority is on shadow unless there's a user preference for daylight.
    /// Some sun on the windows for limited duration is acceptable.
    /// </summary>
    CautiousShadowing,

    /// <summary>
    /// Shadow regardless of user preference for daylight.
    /// No sun on the windows.
    /// </summary>
    AggressiveShadowing,

    /// <summary>
    /// Policy irrelevant becauuse room shutter scene or other input predominates.
    /// </summary>
    PolicyIrrelevant
}
