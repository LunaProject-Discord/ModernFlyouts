namespace ModernFlyouts
{
    public enum FlyoutTriggerType
    {
        Volume,
        Media,
        AirplaneMode,
        Brightness,
        VirtualDesktop
    }

    public class FlyoutTriggerData
    {
        public FlyoutTriggerType TriggerType { get; set; }

        public object Data { get; set; }

        public bool IsExpired;
    }
}
