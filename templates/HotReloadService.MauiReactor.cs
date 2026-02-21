[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(HotReloadService))]

/// <summary>
/// Required for MauiReactor Hot Reload to trigger UI refresh.
/// Add this file to your MauiReactor project and subscribe in your base page:
///   HotReloadService.HotReloadTriggered += () => Invalidate();
/// </summary>
internal static class HotReloadService
{
    public static void ClearCache(Type[]? updatedTypes) { }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        HotReloadTriggered?.Invoke();
    }

    public static event Action? HotReloadTriggered;
}
