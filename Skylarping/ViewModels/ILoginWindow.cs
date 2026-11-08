namespace Skylarping.ViewModels
{
    /// <summary>What the app needs from a theme's sign-in window, whichever theme it is.</summary>
    public interface ILoginWindow
    {
        /// <summary>
        /// Signs in with the saved name without showing the window. Returns false when
        /// there is no saved name.
        /// </summary>
        bool TryAutoLogin();
    }
}
