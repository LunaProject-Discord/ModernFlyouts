namespace ModernFlyouts.Core.Interop
{
    /// <summary>
    /// Window z-order bands (undocumented, see GetWindowBand / CreateWindowInBand).
    /// </summary>
    public enum ZBandID : int
    {
        Default = 0x0,
        Desktop = 0x1,
        UIAccess = 0x2,
        ImmersiveIHM = 0x3,
        ImmersiveNotification = 0x4,
        ImmersiveAppChrome = 0x5,
        ImmersiveMogo = 0x6,
        ImmersiveEdgy = 0x7,
        ImmersiveInActiveMOBODY = 0x8,
        ImmersiveInActiveDock = 0x9,
        ImmersiveActiveMOBODY = 0xA,
        ImmersiveActiveDock = 0xB,
        ImmersiveBackground = 0xC,
        ImmersiveSearch = 0xD,
        GenuineWindows = 0xE,
        ImmersiveRestricted = 0xF,
        SystemTools = 0x10,
        Lock = 0x11,
        AboveLockUX = 0x12,
    }
}
