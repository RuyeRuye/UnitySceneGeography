namespace SceneGeography.Editor
{
    internal enum GeoMarkerState
    {
        Deleted,
        InvalidScene,
        InvalidCapture,
        DuplicateSceneId,
        NoProfile,
        DuplicateProfileId,
        New,
        Modified,
        Synchronized
    }

    internal readonly struct GeoMarkerStatus
    {
        internal GeoMarkerState State { get; }
        internal string Message { get; }
        internal bool IsSynchronized => State == GeoMarkerState.Synchronized;

        internal GeoMarkerStatus(GeoMarkerState state, string message)
        {
            State = state;
            Message = message;
        }
    }
}
